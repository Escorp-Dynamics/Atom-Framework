using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

internal sealed class PageBridgeCommandClient(
    string sessionId,
    string tabId,
    BridgeCommandClient commands,
    Func<CancellationToken, ValueTask>? reapplyTabContextAsync = null,
    Action<Uri?>? trackPendingNavigateUrl = null)
{
    /// <summary>Сколько раз опрашивается готовность вкладки после навигации.</summary>
    private const int BridgeReadinessProbeAttempts = 200;

    private static readonly TimeSpan BridgeReadinessProbeDelay = TimeSpan.FromMilliseconds(50);

    internal Uri GetDiscoveryUrl() => commands.GetDiscoveryUrl();

    public ValueTask<string> GetTitleAsync(CancellationToken cancellationToken = default)
        => commands.GetTitleAsync(sessionId, tabId, cancellationToken);

    public ValueTask<(string TabId, string? WindowId)> OpenTabAsync(string windowId, CancellationToken cancellationToken = default)
        => commands.OpenTabAsync(sessionId, tabId, windowId, cancellationToken);

    public ValueTask<(string TabId, string? WindowId)> OpenWindowAsync(Point? position, CancellationToken cancellationToken = default)
        => commands.OpenWindowAsync(sessionId, tabId, position, cancellationToken);

    public ValueTask<string> GetUrlAsync(CancellationToken cancellationToken = default)
        => commands.GetUrlAsync(sessionId, tabId, cancellationToken);

    public ValueTask<string> GetContentAsync(CancellationToken cancellationToken = default)
        => commands.GetContentAsync(sessionId, tabId, cancellationToken);

    public ValueTask<string?> CaptureScreenshotAsync(CancellationToken cancellationToken = default)
        => commands.CaptureScreenshotAsync(sessionId, tabId, cancellationToken);

    public ValueTask NavigateAsync(Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        return NavigateCoreAsync(url, cancellationToken);
    }

    public ValueTask ReloadAsync(CancellationToken cancellationToken = default)
        => ReloadCoreAsync(cancellationToken);

    public ValueTask<JsonElement> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
        => commands.ExecuteScriptAsync(sessionId, tabId, script, cancellationToken);

    public ValueTask<JsonElement> ExecuteScriptAsync(string script, string shadowHostElementId, CancellationToken cancellationToken = default)
        => commands.ExecuteScriptAsync(sessionId, tabId, script, shadowHostElementId, cancellationToken);

    public ValueTask<JsonElement> ExecuteScriptAsync(
        string script,
        string? shadowHostElementId,
        string? frameHostElementId,
        bool preferPageContextOnNull = false,
        bool forcePageContextExecution = false,
        CancellationToken cancellationToken = default)
        => commands.ExecuteScriptAsync(sessionId, tabId, script, shadowHostElementId, frameHostElementId, preferPageContextOnNull, forcePageContextExecution, cancellationToken);

    public ValueTask<JsonElement> ExecuteScriptAsync(
        string script,
        string? shadowHostElementId,
        string? frameHostElementId,
        string? elementId,
        bool preferPageContextOnNull = false,
        bool forcePageContextExecution = false,
        CancellationToken cancellationToken = default)
        => commands.ExecuteScriptAsync(sessionId, tabId, script, shadowHostElementId, frameHostElementId, elementId, preferPageContextOnNull, forcePageContextExecution, cancellationToken);

    public ValueTask<JsonElement> ExecuteScriptInFramesAsync(
        string script,
        bool isolatedWorld,
        bool includeMetadata,
        CancellationToken cancellationToken = default)
        => commands.ExecuteScriptInFramesAsync(sessionId, tabId, script, isolatedWorld, includeMetadata, cancellationToken);

    public ValueTask SetCookieAsync(
        string contextId,
        string name,
        string value,
        string? domain,
        string? path,
        bool secure,
        bool httpOnly,
        long? expires,
        CancellationToken cancellationToken = default)
        => commands.SetCookieAsync(sessionId, tabId, contextId, name, value, domain, path, secure, httpOnly, expires, cancellationToken);

    public ValueTask<JsonElement> GetCookiesAsync(string contextId, CancellationToken cancellationToken = default)
        => commands.GetCookiesAsync(sessionId, tabId, contextId, cancellationToken);

    public ValueTask DeleteCookiesAsync(string contextId, CancellationToken cancellationToken = default)
        => commands.DeleteCookiesAsync(sessionId, tabId, contextId, cancellationToken);

    public ValueTask SetRequestInterceptionAsync(bool enabled, IEnumerable<string>? urlPatterns, CancellationToken cancellationToken = default)
        => commands.SetRequestInterceptionAsync(sessionId, tabId, enabled, urlPatterns, cancellationToken);

    public ValueTask SetTabContextAsync(JsonObject payload, CancellationToken cancellationToken = default)
        => commands.SetTabContextAsync(sessionId, tabId, payload, cancellationToken);

    public ValueTask CloseWindowAsync(string windowId, CancellationToken cancellationToken = default)
        => commands.CloseWindowAsync(sessionId, tabId, windowId, cancellationToken);

    public ValueTask CloseTabAsync(CancellationToken cancellationToken = default)
        => commands.CloseTabAsync(sessionId, tabId, cancellationToken);

    public ValueTask ActivateWindowAsync(string windowId, CancellationToken cancellationToken = default)
        => commands.ActivateWindowAsync(sessionId, tabId, windowId, cancellationToken);

    public ValueTask ActivateTabAsync(string targetTabId, CancellationToken cancellationToken = default)
        => commands.ActivateTabAsync(sessionId, tabId, ExtractRawTabId(targetTabId), cancellationToken);

    public ValueTask<Rectangle> GetWindowBoundsAsync(CancellationToken cancellationToken = default)
        => commands.GetWindowBoundsAsync(sessionId, tabId, cancellationToken);

    public ValueTask<string?> FindElementAsync(JsonObject payload, CancellationToken cancellationToken = default)
        => commands.FindElementAsync(sessionId, tabId, payload, cancellationToken);

    public ValueTask<string[]> FindElementsAsync(JsonObject payload, CancellationToken cancellationToken = default)
        => commands.FindElementsAsync(sessionId, tabId, payload, cancellationToken);

    public ValueTask<string?> WaitForElementAsync(JsonObject payload, CancellationToken cancellationToken = default)
        => commands.WaitForElementAsync(sessionId, tabId, payload, cancellationToken);

    public ValueTask<string?> GetElementPropertyAsync(string elementId, string propertyName, CancellationToken cancellationToken = default)
        => commands.GetElementPropertyAsync(sessionId, tabId, elementId, propertyName, cancellationToken);

    public ValueTask<bool> CheckShadowRootAsync(string elementId, CancellationToken cancellationToken = default)
        => commands.CheckShadowRootAsync(sessionId, tabId, elementId, cancellationToken);

    public ValueTask<PointF> ResolveElementScreenPointAsync(string elementId, bool scrollIntoView, CancellationToken cancellationToken = default)
        => commands.ResolveElementScreenPointAsync(sessionId, tabId, elementId, scrollIntoView, cancellationToken);

    public ValueTask<BridgeDebugPortStatusPayload> GetDebugPortStatusAsync(CancellationToken cancellationToken = default)
        => commands.GetDebugPortStatusAsync(sessionId, tabId, cancellationToken);

    public ValueTask<BridgeElementDescriptionPayload> DescribeElementAsync(string elementId, CancellationToken cancellationToken = default)
        => commands.DescribeElementAsync(sessionId, tabId, elementId, cancellationToken);

    public ValueTask<BridgeElementDescriptionPayload?> TryDescribeElementAsync(string elementId, CancellationToken cancellationToken = default)
        => commands.TryDescribeElementAsync(sessionId, tabId, elementId, cancellationToken);

    public ValueTask FocusElementAsync(string elementId, bool scrollIntoView, CancellationToken cancellationToken = default)
        => commands.FocusElementAsync(sessionId, tabId, elementId, scrollIntoView, cancellationToken);

    public ValueTask ScrollElementIntoViewAsync(string elementId, CancellationToken cancellationToken = default)
        => commands.ScrollElementIntoViewAsync(sessionId, tabId, elementId, cancellationToken);

    private static string ExtractRawTabId(string value)
    {
        var colonIndex = value.LastIndexOf(':');
        return colonIndex >= 0 ? value[(colonIndex + 1)..] : value;
    }

    private async ValueTask NavigateCoreAsync(Uri url, CancellationToken cancellationToken)
    {
        trackPendingNavigateUrl?.Invoke(url);

        try
        {
            try
            {
                await commands.NavigateAsync(sessionId, tabId, ExtractRawTabId(tabId), url.AbsoluteUri, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception) when (IsExpectedBridgeDisconnect(exception))
            {
                await WaitForBridgeRecoveryAsync("Navigate", exception, cancellationToken).ConfigureAwait(false);
                return;
            }

            await WaitForPostNavigationBridgeTransitionAsync("Navigate", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            trackPendingNavigateUrl?.Invoke(null);
        }
    }

    private async ValueTask ReloadCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await commands.ReloadAsync(sessionId, tabId, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception) when (IsExpectedBridgeDisconnect(exception))
        {
            await WaitForBridgeRecoveryAsync("Reload", exception, cancellationToken).ConfigureAwait(false);
            return;
        }

        await WaitForPostNavigationBridgeTransitionAsync("Reload", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Исход одной пробы состояния мостовой вкладки.</summary>
    private enum BridgeReadinessProbeOutcome
    {
        /// <summary>Вкладка стабилизировалась — ожидание завершено.</summary>
        Settled,

        /// <summary>Нужна ещё одна проба.</summary>
        Retry,

        /// <summary>Вкладка отключилась: требуется переход в режим ожидания восстановления.</summary>
        Disconnected,
    }

    private sealed class BridgeReadinessProbeState
    {
        public bool BridgeTransitionObserved { get; set; }

        public bool TabContextReapplied { get; set; }

        public BridgeDebugPortStatusPayload? LastStatus { get; set; }

        /// <summary>Отключение, обнаруженное последней пробой.</summary>
        public InvalidOperationException? PendingDisconnect { get; set; }
    }

    private async ValueTask WaitForPostNavigationBridgeTransitionAsync(string commandName, CancellationToken cancellationToken)
    {
        var state = new BridgeReadinessProbeState
        {
            TabContextReapplied = reapplyTabContextAsync is null,
        };

        for (var attempt = 0; attempt < BridgeReadinessProbeAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = await ProbeBridgeReadinessAsync(commandName, state, requireReapplyAfterTransition: false, cancellationToken).ConfigureAwait(false);
            if (outcome is BridgeReadinessProbeOutcome.Settled)
                return;

            if (outcome is BridgeReadinessProbeOutcome.Disconnected)
            {
                await WaitForBridgeRecoveryAsync(commandName, state.PendingDisconnect!, cancellationToken).ConfigureAwait(false);
                return;
            }

            await Task.Delay(BridgeReadinessProbeDelay, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"Мостовая вкладка '{tabId}' не вернулась в состояние ready после команды '{commandName}'. Последний DebugPortStatus: {FormatDebugPortStatus(state.LastStatus)}; bridgeTransitionObserved={FormatBoolean(state.BridgeTransitionObserved)}; tabContextReapplied={FormatBoolean(state.TabContextReapplied)}.");
    }

    private async ValueTask WaitForBridgeRecoveryAsync(string commandName, InvalidOperationException originalException, CancellationToken cancellationToken)
    {
        var state = new BridgeReadinessProbeState
        {
            BridgeTransitionObserved = true,
            TabContextReapplied = reapplyTabContextAsync is null,
        };

        for (var attempt = 0; attempt < BridgeReadinessProbeAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // В режиме восстановления повторное отключение — ожидаемый шаг, а не выход из цикла.
            var outcome = await ProbeBridgeReadinessAsync(commandName, state, requireReapplyAfterTransition: true, cancellationToken).ConfigureAwait(false);
            if (outcome is BridgeReadinessProbeOutcome.Settled)
                return;

            await Task.Delay(BridgeReadinessProbeDelay, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"Мостовая вкладка '{tabId}' не переподключилась после ожидаемого отключения во время команды '{commandName}'. Последний DebugPortStatus: {FormatDebugPortStatus(state.LastStatus)}; bridgeTransitionObserved={FormatBoolean(state.BridgeTransitionObserved)}; tabContextReapplied={FormatBoolean(state.TabContextReapplied)}.",
            originalException);
    }

    private async ValueTask<BridgeReadinessProbeOutcome> ProbeBridgeReadinessAsync(
        string commandName,
        BridgeReadinessProbeState state,
        bool requireReapplyAfterTransition,
        CancellationToken cancellationToken)
    {
        // Отключение может прийти и из повторного применения контекста вкладки, а не только
        // из самой пробы, поэтому обе операции обрабатываются одним catch-контуром.
        try
        {
            var status = await commands.GetDebugPortStatusAsync(sessionId, tabId, cancellationToken).ConfigureAwait(false);
            state.LastStatus = status;

            var isReady = status.HasPort && status.HasSocket && status.IsReady;
            if (!isReady)
                state.BridgeTransitionObserved = true;

            if (ShouldYieldPendingNavigateToCaller(commandName, status, state.BridgeTransitionObserved))
                return BridgeReadinessProbeOutcome.Settled;

            if (state.BridgeTransitionObserved && !state.TabContextReapplied)
                state.TabContextReapplied = await TryReapplyTabContextIfNeededAsync(cancellationToken).ConfigureAwait(false);

            var reapplySatisfied = state.TabContextReapplied
                || (!requireReapplyAfterTransition && !state.BridgeTransitionObserved);

            return isReady && reapplySatisfied
                ? BridgeReadinessProbeOutcome.Settled
                : BridgeReadinessProbeOutcome.Retry;
        }
        catch (InvalidOperationException exception) when (IsExpectedBridgeDisconnect(exception))
        {
            state.BridgeTransitionObserved = true;
            state.PendingDisconnect = exception;
            return BridgeReadinessProbeOutcome.Disconnected;
        }
        catch (InvalidOperationException exception) when (IsUnansweredBridgeProbe(exception))
        {
            _ = exception;
            return BridgeReadinessProbeOutcome.Settled;
        }
    }

    private static string FormatDebugPortStatus(BridgeDebugPortStatusPayload? status)
    {
        if (status is null)
            return "<none>";

        return "hasPort=" + FormatBoolean(status.HasPort)
            + ", hasSocket=" + FormatBoolean(status.HasSocket)
            + ", isReady=" + FormatBoolean(status.IsReady)
            + ", queueLength=" + status.QueueLength.ToString(CultureInfo.InvariantCulture)
            + ", interceptEnabled=" + FormatBoolean(status.InterceptEnabled)
            + ", hasTabContext=" + FormatBoolean(status.HasTabContext)
            + ", contextId=" + (status.ContextId ?? "<null>")
            + ", contextUserAgent=" + (status.ContextUserAgent ?? "<null>")
            + ", hasBrowserTab=" + FormatBoolean(status.HasBrowserTab)
            + ", browserTabUrl=" + (status.BrowserTabUrl ?? "<null>")
            + ", browserTabPendingUrl=" + (status.BrowserTabPendingUrl ?? "<null>")
                + ", browserTabStatus=" + (status.BrowserTabStatus ?? "<null>")
                + ", runtimeCheckStatus=" + (status.RuntimeCheckStatus ?? "<null>")
                + ", runtimeHref=" + (status.RuntimeHref ?? "<null>")
                + ", runtimeReadyState=" + (status.RuntimeReadyState ?? "<null>")
                + ", runtimeCheckError=" + (status.RuntimeCheckError ?? "<null>");
    }

    private static bool ShouldYieldPendingNavigateToCaller(
        string commandName,
        BridgeDebugPortStatusPayload status,
        bool bridgeTransitionObserved)
        => string.Equals(commandName, "Navigate", StringComparison.Ordinal)
            && bridgeTransitionObserved
            && !status.HasPort
            && !status.HasSocket
            && !status.IsReady
            && status.HasTabContext
            && status.HasBrowserTab
            && !string.IsNullOrWhiteSpace(status.BrowserTabPendingUrl)
            && string.Equals(status.BrowserTabStatus, "loading", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(status.RuntimeCheckStatus, "navigate-in-flight-grace", StringComparison.Ordinal)
                || string.Equals(status.RuntimeCheckStatus, "url-mismatch", StringComparison.Ordinal));

    private static string FormatBoolean(bool value)
        => value ? "true" : "false";

    private async ValueTask<bool> TryReapplyTabContextIfNeededAsync(CancellationToken cancellationToken)
    {
        if (reapplyTabContextAsync is null)
            return true;

        try
        {
            await reapplyTabContextAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (InvalidOperationException exception) when (IsExpectedBridgeDisconnect(exception))
        {
            _ = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// Ожидаемое отключение вкладки или сеанса. Классификация идёт по типизированным данным
    /// исключения: подстрочный поиск по локализованному сообщению ломался бы от любой
    /// переформулировки текста.
    /// </summary>
    private static bool IsExpectedBridgeDisconnect(InvalidOperationException exception)
        => BridgeCommandException.IsSurfaceDisconnect(exception);

    /// <summary>
    /// Проба готовности вкладки не получила ответа за отведённое время.
    /// </summary>
    /// <remarks>
    /// Ожидание готовности — это стабилизация после уже выполненной команды, а не сама команда.
    /// Во время перезагрузки контентный слой вкладки уничтожается и пересоздаётся, поэтому
    /// DebugPortStatus законно может не ответить. Раньше такой таймаут поднимался наружу и
    /// ронял весь Navigate/Reload, хотя сама навигация к тому моменту уже была подтверждена
    /// мостом. Теперь стабилизация просто завершается по факту.
    /// </remarks>
    private static bool IsUnansweredBridgeProbe(InvalidOperationException exception)
        => exception is BridgeCommandException { Status: BridgeStatus.Timeout };
}