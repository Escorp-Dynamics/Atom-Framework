using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет изолированное WebSocket-соединение с одной вкладкой браузера.
/// </summary>
/// <remarks>
/// Каждый экземпляр инкапсулирует двустороннюю связь с конкретной вкладкой,
/// обеспечивая независимую отправку команд и приём событий.
/// </remarks>
/// <param name="tabId">Идентификатор вкладки.</param>
/// <param name="socket">WebSocket-соединение.</param>
/// <param name="requestTimeout">Таймаут ожидания ответа.</param>
/// <param name="maxMessageSize">Максимальный размер сообщения.</param>
internal sealed class TabChannel(string tabId, WebSocket socket, TimeSpan requestTimeout, int maxMessageSize) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BridgeMessage>> pending = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource cts = new();
    private Task? receiveLoop;
    private bool isDisposed;

    /// <summary>
    /// Идентификатор вкладки.
    /// </summary>
    public string TabId { get; } = tabId;

    /// <summary>
    /// Определяет, активно ли соединение.
    /// </summary>
    public bool IsConnected => !isDisposed && socket.State == WebSocketState.Open;

    /// <summary>
    /// Происходит при получении события от расширения.
    /// </summary>
    public event AsyncEventHandler<TabChannel, TabChannelEventArgs>? EventReceived;

    /// <summary>
    /// Происходит при разрыве соединения.
    /// </summary>
    public event AsyncEventHandler<TabChannel, EventArgs>? Disconnected;

    /// <summary>
    /// Запускает цикл приёма сообщений от расширения.
    /// </summary>
    public void StartReceiving() => receiveLoop = Task.Run(() => ReceiveLoopAsync(cts.Token));

    /// <summary>
    /// Отправляет команду расширению и ожидает ответ.
    /// </summary>
    /// <param name="command">Команда для выполнения.</param>
    /// <param name="payload">Полезная нагрузка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Ответное сообщение от расширения.</returns>
    public async ValueTask<BridgeMessage> SendCommandAsync(
        BridgeCommand command,
        object? payload = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!IsConnected)
            throw new BridgeException($"Вкладка '{TabId}' не подключена.");

        var messageId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<BridgeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[messageId] = tcs;

        try
        {
            var message = new BridgeMessage
            {
                Id = messageId,
                Type = BridgeMessageType.Request,
                TabId = TabId,
                Command = command,
                Payload = payload,
            };

            await SendMessageAsync(message, cancellationToken).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(requestTimeout);

            return await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BridgeException($"Истёк таймаут ожидания ответа от вкладки '{TabId}'.");
        }
        finally
        {
            pending.TryRemove(messageId, out _);
        }
    }

#pragma warning disable MA0038 // Ложное срабатывание: метод использует параметр socket из primary constructor.
    private async Task SendMessageAsync(BridgeMessage message, CancellationToken cancellationToken)
#pragma warning restore MA0038
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, BridgeJsonContext.Default.BridgeMessage);
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[maxMessageSize];

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var (count, messageType) = await ReceiveFullMessageAsync(buffer, cancellationToken).ConfigureAwait(false);

                if (messageType == WebSocketMessageType.Close)
                    break;

                if (messageType != WebSocketMessageType.Text)
                    continue;

                var message = JsonSerializer.Deserialize(
                    buffer.AsSpan(0, count),
                    BridgeJsonContext.Default.BridgeMessage);

                if (message is null)
                    continue;

                await HandleIncomingMessageAsync(message).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Штатное завершение.
        }
        catch (WebSocketException)
        {
            // Соединение разорвано.
        }
        finally
        {
            CompletePendingRequests();

            if (Disconnected is { } handler)
                await handler(this, EventArgs.Empty).ConfigureAwait(false);
        }
    }

#pragma warning disable MA0038 // Ложное срабатывание: метод использует параметр socket из primary constructor.
    private async ValueTask<(int Count, WebSocketMessageType MessageType)> ReceiveFullMessageAsync(byte[] buffer, CancellationToken cancellationToken)
#pragma warning restore MA0038
    {
        var totalBytes = 0;

        ValueWebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(
                buffer.AsMemory(totalBytes),
                cancellationToken).ConfigureAwait(false);

            totalBytes += result.Count;

            if (totalBytes >= buffer.Length && !result.EndOfMessage)
                throw new BridgeException("Размер сообщения превышает допустимый предел.");
        }
        while (!result.EndOfMessage);

        return (totalBytes, result.MessageType);
    }

    private async ValueTask HandleIncomingMessageAsync(BridgeMessage message)
    {
        switch (message.Type)
        {
            case BridgeMessageType.Response when pending.TryRemove(message.Id, out var tcs):
                tcs.TrySetResult(message);
                break;

            case BridgeMessageType.Event when EventReceived is { } handler:
                await handler(this, new TabChannelEventArgs(message)).ConfigureAwait(false);
                break;

            case BridgeMessageType.Pong:
                // Подтверждение пинга — ничего не делаем.
                break;
        }
    }

    private void CompletePendingRequests()
    {
        foreach (var kvp in pending)
        {
            kvp.Value.TrySetException(new BridgeException($"Соединение с вкладкой '{TabId}' разорвано."));
        }

        pending.Clear();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;

        await cts.CancelAsync().ConfigureAwait(false);

        if (socket.State == WebSocketState.Open)
        {
            try
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Драйвер завершает работу.",
                    closeCts.Token).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                // Соединение уже закрыто.
            }
            catch (OperationCanceledException)
            {
                // Close handshake не завершился за 5 с — закрываем принудительно.
            }
        }

        if (receiveLoop is not null)
        {
#pragma warning disable VSTHRD003 // Цикл приёма запущен в нашем контексте через StartReceiving.
            try
            {
                await receiveLoop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Штатное завершение цикла приёма.
            }
            catch (TimeoutException)
            {
                // Цикл приёма не завершился за 5 с — пропускаем.
            }
#pragma warning restore VSTHRD003
        }

        socket.Dispose();
        cts.Dispose();
    }
}
