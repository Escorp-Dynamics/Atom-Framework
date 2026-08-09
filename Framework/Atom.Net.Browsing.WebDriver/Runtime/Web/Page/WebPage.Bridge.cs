using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

public sealed partial class WebPage
{
    internal PageBridgeCommandClient? BridgeCommands { get; private set; }

    internal string? BoundBridgeSessionId { get; private set; }

    internal string? BoundBridgeTabId { get; private set; }

    private string? BridgeContextId { get; set; }

    // Навигация/перезагрузка пересоздаёт порт content-скрипта: на это окно вкладка временно
    // «исчезает» из мостового реестра (TabDisconnected приходит раньше повторного TabConnected при
    // переупорядочивании под нагрузкой), и адресованные ей команды отклоняются как surface-disconnect.
    // Бюджет повторов (≈5 c) с запасом покрывает переподключение вкладки; тот же порядок величины,
    // что и ожидание готовности вкладки после навигации.
    private const int RequestInterceptionReconnectRetryAttempts = 100;

    private static readonly TimeSpan RequestInterceptionReconnectRetryDelay = TimeSpan.FromMilliseconds(50);

    internal RequestInterceptionState? GetEffectiveRequestInterceptionState()
        => requestInterceptionState ?? OwnerWindow.GetEffectiveRequestInterceptionState();

    internal async ValueTask ApplyEffectiveRequestInterceptionAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var effectiveState = GetEffectiveRequestInterceptionState();
        if (BridgeCommands is not { } bridge)
            return;

        if (RequestInterceptionState.AreEquivalent(appliedRequestInterceptionState, effectiveState))
            return;

        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                await bridge.SetRequestInterceptionAsync(
                    effectiveState?.Enabled ?? false,
                    effectiveState?.UrlPatterns,
                    cancellationToken).ConfigureAwait(false);

                // Навигационный режим (proxy/webrequest) вычисляется в момент отправки контекста вкладки,
                // поэтому после включения/выключения перехвата контекст нужно переотправить.
                await WebBrowser.ApplyBridgeTabContextAsync(this, cancellationToken).ConfigureAwait(false);

                appliedRequestInterceptionState = effectiveState;
                return;
            }
            catch (InvalidOperationException exception)
                when (attempt < RequestInterceptionReconnectRetryAttempts
                    && BridgeCommandException.IsSurfaceDisconnect(exception))
            {
                // Вкладка между документами: обе команды выше идемпотентны, поэтому ждём переподключения
                // и повторяем, а не роняем обновление перехвата, случайно совпавшее с навигацией соседней
                // страницы. ThrowIfDisposed прекращает повтор, если саму страницу закрыли.
                ThrowIfDisposed();
                await Task.Delay(RequestInterceptionReconnectRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal void BindBridgeCommands(string sessionId, BridgeCommandClient commands)
        => BindBridgeCommands(sessionId, TabId, commands);

    internal void BindBridgeCommands(string sessionId, string tabId, BridgeCommandClient commands)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        ArgumentNullException.ThrowIfNull(commands);
        ThrowIfDisposed();

        BoundBridgeSessionId = sessionId;
        BoundBridgeTabId = tabId;
        BridgeCommands = new PageBridgeCommandClient(
            sessionId,
            tabId,
            commands,
            cancellationToken => commands.SetTabContextAsync(
                sessionId,
                tabId,
                WebBrowser.BuildSetTabContextPayload(this),
                cancellationToken),
            trackPendingNavigateUrl: SetPendingBridgeNavigationUrl);
        appliedRequestInterceptionState = null;
    }
}