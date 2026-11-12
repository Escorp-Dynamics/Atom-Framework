using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Net.Browsing.WebDriver.Protocol;
using Atom.Net.Browsing.WebDriver.Protocol.Cdp;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Вкладка Obscura: цель CDP, её flat-сессия и состояние, которое транспорт ведёт за страницу.
/// </summary>
internal sealed class CdpTab(CdpServerSlot server, string tabId, string windowId, string targetId, string sessionId, string contextId)
{
    private readonly Lock gate = new();
    private Task callbackChain = Task.CompletedTask;
    private Task eventChain = Task.CompletedTask;
    private PendingDocument? pendingDocument;
    private volatile bool isClosed;

    public CdpServerSlot Server { get; } = server;

    /// <summary>Идентификатор вкладки драйвера (уникален в пределах транспорта).</summary>
    public string TabId { get; } = tabId;

    /// <summary>Логическое окно драйвера, к которому относится вкладка.</summary>
    public string WindowId { get; } = windowId;

    public string TargetId { get; } = targetId;

    /// <summary>Flat-сессия CDP вкладки.</summary>
    public string SessionId { get; } = sessionId;

    /// <summary>Браузерный контекст Obscura (хранилище cookie вкладки).</summary>
    public string ContextId { get; } = contextId;

    public DateTimeOffset RegisteredAtUtc { get; } = DateTimeOffset.UtcNow;

    public WebPage? Page { get; set; }

    public bool IsClosed => isClosed;

    /// <summary>Состояние перехвата, которое страница передала последним.</summary>
    public RequestInterceptionState? Interception { get; set; }

    /// <summary>Включён ли <c lang="text">Fetch</c> в сессии вкладки (остаётся включённым до закрытия).</summary>
    public bool IsFetchEnabled { get; set; }

    /// <summary>Подпись последнего применённого профиля личности.</summary>
    public string? IdentitySignature { get; set; }

    /// <summary>Идентификатор скрипта личности в <c lang="text">Page.addScriptToEvaluateOnNewDocument</c>.</summary>
    public string? IdentityScriptId { get; set; }

    public SemaphoreSlim ContextGate { get; } = new(1, 1);

    public void MarkClosed() => isClosed = true;

    public void SetPendingDocument(Uri url, byte[] body)
    {
        lock (gate)
            pendingDocument = new PendingDocument(url.AbsoluteUri, body);
    }

    public void ClearPendingDocument(Uri url)
    {
        lock (gate)
        {
            if (pendingDocument is { } current && string.Equals(current.Url, url.AbsoluteUri, StringComparison.Ordinal))
                pendingDocument = null;
        }
    }

    /// <summary>Забирает подготовленный документ, если запрос адресован ему.</summary>
    public byte[]? TakePendingDocument(string url)
    {
        lock (gate)
        {
            if (pendingDocument is not { } current || !IsSameDocumentUrl(current.Url, url))
                return null;

            pendingDocument = null;
            return current.Body;
        }
    }

    /// <summary>
    /// Ставит обработку callback'а в очередь вкладки: события одного вызова (запрос, завершение)
    /// доходят в порядке страницы, но не задерживают остальные события транспорта.
    /// </summary>
    public void EnqueueCallbackWork(Func<Task> work)
    {
        lock (gate)
            callbackChain = RunAfterAsync(callbackChain, work);
    }

    /// <summary>
    /// Ставит обработку события жизненного цикла или консоли в очередь вкладки, сохраняя порядок
    /// событий страницы (DOMContentLoaded раньше load).
    /// </summary>
    public void EnqueueEventWork(Func<Task> work)
    {
        lock (gate)
            eventChain = RunAfterAsync(eventChain, work);
    }

    /// <summary>
    /// Ждёт, пока будут опубликованы все события, полученные до этого момента.
    /// </summary>
    /// <remarks>
    /// Obscura отвечает на навигацию после load, а события приходят раньше ответа, поэтому барьер
    /// после ответа гарантирует, что вызывающий увидит события своей навигации до возврата.
    /// </remarks>
    public Task FlushEventsAsync()
    {
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EnqueueEventWork(() =>
        {
            barrier.TrySetResult();
            return Task.CompletedTask;
        });

        return barrier.Task;
    }

    public BridgeTabChannelSnapshot CreateSnapshot(string sessionId)
        => new(sessionId, TabId, WindowId, RegisteredAtUtc, DateTimeOffset.UtcNow, IsRegistered: !IsClosed);

    /// <summary>
    /// Команда в сессию вкладки с переводом ошибок протокола в исключения моста.
    /// </summary>
    public ValueTask<JsonElement> SendAsync(string method, JsonObject? parameters, CancellationToken cancellationToken)
        => SendAsync(method, parameters, timeout: null, cancellationToken);

    public async ValueTask<JsonElement> SendAsync(string method, JsonObject? parameters, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        if (IsClosed)
            throw CreateDisconnectedException(innerException: null);

        try
        {
            return await Server.Connection.SendAsync(method, parameters, SessionId, timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (CdpException exception) when (IsMissingSurface(exception))
        {
            MarkClosed();
            throw CreateDisconnectedException(exception);
        }
        catch (CdpException exception)
        {
            throw new BridgeCommandException(exception.ServerMessage ?? exception.Message, BridgeStatus.Error, innerException: exception);
        }
        catch (TimeoutException exception)
        {
            throw new BridgeCommandException(exception.Message, BridgeStatus.Timeout, innerException: exception);
        }
        catch (ObjectDisposedException exception)
        {
            throw CreateDisconnectedException(exception);
        }
        catch (InvalidOperationException exception) when (exception is not BridgeCommandException && !Server.Connection.IsOpen)
        {
            throw CreateDisconnectedException(exception);
        }
    }

    internal static bool IsMissingSurface(CdpException exception)
        => exception.ServerMessage is { } message
            && (message.Contains("No page", StringComparison.Ordinal)
                || message.Contains("Target not found", StringComparison.Ordinal)
                || message.Contains("Session not found", StringComparison.Ordinal));

    private BridgeCommandException CreateDisconnectedException(Exception? innerException)
        => new($"Вкладка Obscura '{TabId}' закрыта или недоступна", BridgeStatus.Disconnected, isSurfaceDisconnected: true, innerException: innerException);

    // Фрагмент в запрос не попадает (CDP передаёт его отдельно), поэтому сравнение без него.
    private static bool IsSameDocumentUrl(string expected, string actual)
        => Uri.TryCreate(expected, UriKind.Absolute, out var expectedUri)
            && Uri.TryCreate(actual, UriKind.Absolute, out var actualUri)
            && string.Equals(expectedUri.GetLeftPart(UriPartial.Query), actualUri.GetLeftPart(UriPartial.Query), StringComparison.Ordinal);

    private static async Task RunAfterAsync(Task previous, Func<Task> work)
    {
        try
        {
#pragma warning disable VSTHRD003 // Цепочка принадлежит вкладке и состоит только из её же задач.
            await previous.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Сбой предыдущего звена уже записан в журнал им самим и не должен рвать цепочку.
        }

        await work().ConfigureAwait(false);
    }

    private sealed record PendingDocument(string Url, byte[] Body);
}
