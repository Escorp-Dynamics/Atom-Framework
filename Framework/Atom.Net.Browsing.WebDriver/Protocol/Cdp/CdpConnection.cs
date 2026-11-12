using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Atom.Net.Browsing.WebDriver.Protocol.Cdp;

/// <summary>
/// Соединение с сервером протокола отладки Chrome (CDP) поверх WebSocket.
/// </summary>
/// <remarks>
/// Одно соединение обслуживает браузерную цель и все flat-сессии вкладок
/// (<c lang="text">Target.attachToTarget</c> с <c lang="text">flatten: true</c>): команды адресуются полем <c lang="text">sessionId</c>,
/// ответы сопоставляются по <c lang="text">id</c>, события рассылаются через <see cref="EventReceived"/>.
/// </remarks>
internal sealed class CdpConnection : IAsyncDisposable
{
    private const int ReceiveBufferSize = 64 * 1024;

    private readonly WebSocket socket;
    private readonly ConcurrentDictionary<long, PendingCommand> pending = new();
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task receiveLoop;
    private long nextId;
    private int disposeState;
    private Exception? terminalError;

    private CdpConnection(WebSocket socket)
    {
        this.socket = socket;
        receiveLoop = Task.Run(ReceiveLoopAsync, CancellationToken.None);
    }

    /// <summary>
    /// Событие протокола. Обработчик вызывается в потоке приёма и не должен блокироваться.
    /// </summary>
    public event Action<CdpEvent>? EventReceived;

    /// <summary>
    /// Срабатывает один раз, когда соединение закрыто сервером или разорвано.
    /// </summary>
    public event Action<Exception?>? Closed;

    /// <summary>Тайм-аут команды по умолчанию.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public bool IsOpen => Volatile.Read(ref disposeState) == 0 && socket.State == WebSocketState.Open;

    /// <summary>
    /// Подключается к WebSocket-адресу отладчика.
    /// </summary>
    public static async ValueTask<CdpConnection> ConnectAsync(Uri endpoint, string? bearerToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var client = new ClientWebSocket();
        try
        {
            client.Options.KeepAliveInterval = TimeSpan.Zero;
            if (!string.IsNullOrEmpty(bearerToken))
                client.Options.SetRequestHeader("Authorization", "Bearer " + bearerToken);

            await client.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            return new CdpConnection(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Оборачивает уже установленный WebSocket (используется тестами с серверной стороной в памяти).
    /// </summary>
    internal static CdpConnection Attach(WebSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        return new CdpConnection(socket);
    }

    /// <summary>
    /// Отправляет команду и ожидает её результат.
    /// </summary>
    /// <returns>Поле <c lang="text">result</c> ответа (отсоединённая копия).</returns>
    /// <exception cref="CdpException">Сервер вернул ошибку.</exception>
    /// <exception cref="TimeoutException">Ответ не пришёл за <see cref="CommandTimeout"/>.</exception>
    public async ValueTask<JsonElement> SendAsync(
        string method,
        JsonObject? parameters = null,
        string? sessionId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ThrowIfUnavailable();

        var id = Interlocked.Increment(ref nextId);
        var command = new PendingCommand(method);
        pending[id] = command;

        try
        {
            var payload = SerializeCommand(id, method, parameters, sessionId);

            await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfUnavailable();
                // Отмена посреди отправки рвёт WebSocket целиком (и все вкладки процесса), поэтому
                // отменяемы только ожидание очереди и ответа, а сама запись — лишь освобождением.
                await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, lifetime.Token).ConfigureAwait(false);
            }
            finally
            {
                sendGate.Release();
            }

            var effectiveTimeout = timeout ?? CommandTimeout;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            deadline.CancelAfter(effectiveTimeout);

            try
            {
                return await command.Completion.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && !lifetime.IsCancellationRequested)
            {
                throw new TimeoutException(
                    string.Create(CultureInfo.InvariantCulture, $"Команда CDP '{method}' не получила ответ за {effectiveTimeout.TotalSeconds:0.#} с"),
                    exception);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw CreateClosedException();
            }
        }
        finally
        {
            pending.TryRemove(id, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeState, 1) != 0)
            return;

        await lifetime.CancelAsync().ConfigureAwait(false);

        if (socket.State == WebSocketState.Open)
        {
            try
            {
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, closeTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                // Сервер мог уже закрыть соединение — для освобождения это не ошибка.
            }
        }

        socket.Abort();

        try
        {
#pragma warning disable VSTHRD003 // Цикл приёма принадлежит этому объекту и запускается в его конструкторе.
            await receiveLoop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Цикл приёма завершается вместе с сокетом; его сбой уже передан через OnTerminated,
            // а освобождение обязано дойти до конца, иначе процесс-владелец не будет остановлен.
        }

        FailPending(new ObjectDisposedException(nameof(CdpConnection)));
        socket.Dispose();
        sendGate.Dispose();
        lifetime.Dispose();
    }

    private static byte[] SerializeCommand(long id, string method, JsonObject? parameters, string? sessionId)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", id);
            writer.WriteString("method", method);
            writer.WritePropertyName("params");
            if (parameters is null)
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
            }
            else
            {
                parameters.WriteTo(writer);
            }

            if (!string.IsNullOrEmpty(sessionId))
                writer.WriteString("sessionId", sessionId);

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        using var message = new MemoryStream();
        Exception? failure = null;

        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                message.SetLength(0);
                ValueWebSocketReceiveResult result;

                do
                {
                    result = await socket.ReceiveAsync(buffer.AsMemory(), lifetime.Token).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                        return;

                    message.Write(buffer.AsSpan(0, result.Count));
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                    Dispatch(message.GetBuffer().AsMemory(0, (int)message.Length));
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // Штатное освобождение.
        }
        catch (Exception exception) when (exception is WebSocketException or IOException or ObjectDisposedException)
        {
            failure = exception;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            OnTerminated(failure);
        }
    }

    private void Dispatch(ReadOnlyMemory<byte> utf8)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8);
        }
        catch (JsonException)
        {
            // Повреждённое сообщение сервера не должно ронять цикл приёма.
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return;

            if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id))
            {
                CompleteCommand(id, root);
                return;
            }

            if (!root.TryGetProperty("method", out var methodElement) || methodElement.GetString() is not { Length: > 0 } method)
                return;

            var parameters = root.TryGetProperty("params", out var paramsElement)
                ? paramsElement.Clone()
                : EmptyObject();
            var sessionId = root.TryGetProperty("sessionId", out var sessionElement) ? sessionElement.GetString() : null;

            try
            {
                EventReceived?.Invoke(new CdpEvent(method, parameters, sessionId));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Подписчик не должен ронять цикл приёма: содержимое событий частично задаёт страница.
            }
        }
    }

    private void CompleteCommand(long id, JsonElement root)
    {
        if (!pending.TryRemove(id, out var command))
            return;

        if (root.TryGetProperty("error", out var error))
        {
            var code = error.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsedCode) ? parsedCode : 0;
            var text = error.TryGetProperty("message", out var messageElement) ? messageElement.GetString() ?? string.Empty : string.Empty;
            command.Completion.TrySetException(new CdpException(command.Method, code, text));
            return;
        }

        var result = root.TryGetProperty("result", out var resultElement) ? resultElement.Clone() : EmptyObject();
        command.Completion.TrySetResult(result);
    }

    private void OnTerminated(Exception? failure)
    {
        terminalError = failure;
        if (!lifetime.IsCancellationRequested)
        {
            try
            {
                lifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Освобождение уже идёт.
            }
        }

        FailPending(CreateClosedException());
        Closed?.Invoke(failure);
    }

    private void FailPending(Exception exception)
    {
        foreach (var entry in pending)
        {
            if (pending.TryRemove(entry.Key, out var command))
                command.Completion.TrySetException(exception);
        }
    }

    private InvalidOperationException CreateClosedException()
        => new("Соединение CDP закрыто", terminalError);

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposeState) != 0, this);
        if (lifetime.IsCancellationRequested)
            throw CreateClosedException();
    }

    private static JsonElement EmptyObject()
    {
        using var document = JsonDocument.Parse("{}"u8.ToArray());
        return document.RootElement.Clone();
    }

    private sealed class PendingCommand(string method)
    {
        public string Method { get; } = method;

        public TaskCompletionSource<JsonElement> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
