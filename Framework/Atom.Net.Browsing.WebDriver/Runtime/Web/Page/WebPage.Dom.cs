using System.Drawing;
using System.Globalization;
using System.Text.Json;

namespace Atom.Net.Browsing.WebDriver;

public sealed partial class WebPage
{
    public ValueTask<Uri?> GetUrlAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return MainFrame.GetUrlAsync(cancellationToken);
    }

    public ValueTask<Uri?> GetUrlAsync()
        => GetUrlAsync(CancellationToken.None);

    public ValueTask<string?> GetTitleAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return MainFrame.GetTitleAsync(cancellationToken);
    }

    public ValueTask<string?> GetTitleAsync()
        => GetTitleAsync(CancellationToken.None);

    public ValueTask<string?> GetContentAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return MainFrame.GetContentAsync(cancellationToken);
    }

    public ValueTask<string?> GetContentAsync()
        => GetContentAsync(CancellationToken.None);

    public ValueTask<JsonElement?> EvaluateAsync(string script, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(script);
        ThrowIfDisposed();
        return MainFrame.EvaluateAsync(script, cancellationToken);
    }

    public ValueTask<JsonElement?> EvaluateAsync(string script)
        => EvaluateAsync(script, CancellationToken.None);

    public ValueTask<TResult?> EvaluateAsync<TResult>(string script, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(script);
        ThrowIfDisposed();
        return MainFrame.EvaluateAsync<TResult>(script, cancellationToken);
    }

    public ValueTask<TResult?> EvaluateAsync<TResult>(string script)
        => EvaluateAsync<TResult>(script, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(string selector, CancellationToken cancellationToken)
        => WaitForElementAsync(new CssSelector(selector), WaitForElementKind.Attached, WaitingTimeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(string selector)
        => WaitForElementAsync(selector, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(string selector, TimeSpan timeout, CancellationToken cancellationToken)
        => WaitForElementAsync(new CssSelector(selector), WaitForElementKind.Attached, timeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(string selector, TimeSpan timeout)
        => WaitForElementAsync(selector, timeout, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(string selector, WaitForElementKind kind, CancellationToken cancellationToken)
        => WaitForElementAsync(new CssSelector(selector), kind, WaitingTimeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(string selector, WaitForElementKind kind)
        => WaitForElementAsync(selector, kind, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(string selector, WaitForElementKind kind, TimeSpan timeout, CancellationToken cancellationToken)
        => WaitForElementAsync(new CssSelector(selector), kind, timeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(string selector, WaitForElementKind kind, TimeSpan timeout)
        => WaitForElementAsync(selector, kind, timeout, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(WaitForElementSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfDisposed();
        return WaitForElementAsync(settings.Selector ?? new CssSelector(string.Empty), settings.Kind, settings.Timeout ?? WaitingTimeout, cancellationToken);
    }

    public ValueTask<IElement?> WaitForElementAsync(WaitForElementSettings settings)
        => WaitForElementAsync(settings, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, CancellationToken cancellationToken)
        => WaitForElementAsync(selector, WaitForElementKind.Attached, WaitingTimeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector)
        => WaitForElementAsync(selector, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, TimeSpan timeout, CancellationToken cancellationToken)
        => WaitForElementAsync(selector, WaitForElementKind.Attached, timeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, TimeSpan timeout)
        => WaitForElementAsync(selector, timeout, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, WaitForElementKind kind, CancellationToken cancellationToken)
        => WaitForElementAsync(selector, kind, WaitingTimeout, cancellationToken);

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, WaitForElementKind kind)
        => WaitForElementAsync(selector, kind, CancellationToken.None);

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, WaitForElementKind kind, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ThrowIfDisposed();
        return MainFrame.WaitForElementAsync(selector, kind, timeout, cancellationToken);
    }

    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, WaitForElementKind kind, TimeSpan timeout)
        => WaitForElementAsync(selector, kind, timeout, CancellationToken.None);

    public ValueTask<IElement?> GetElementAsync(string selector, CancellationToken cancellationToken)
        => GetElementAsync(new CssSelector(selector), cancellationToken);

    public ValueTask<IElement?> GetElementAsync(string selector)
        => GetElementAsync(selector, CancellationToken.None);

    public ValueTask<IElement?> GetElementAsync(CssSelector selector, CancellationToken cancellationToken)
        => GetElementAsync((ElementSelector)selector, cancellationToken);

    public ValueTask<IElement?> GetElementAsync(CssSelector selector)
        => GetElementAsync(selector, CancellationToken.None);

    public ValueTask<IElement?> GetElementAsync(ElementSelector selector, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ThrowIfDisposed();
        return MainFrame.GetElementAsync(selector, cancellationToken);
    }

    public ValueTask<IElement?> GetElementAsync(ElementSelector selector)
        => GetElementAsync(selector, CancellationToken.None);

    public ValueTask<IEnumerable<IElement>> GetElementsAsync(string selector, CancellationToken cancellationToken)
        => GetElementsAsync(new CssSelector(selector), cancellationToken);

    public ValueTask<IEnumerable<IElement>> GetElementsAsync(string selector)
        => GetElementsAsync(selector, CancellationToken.None);

    public ValueTask<IEnumerable<IElement>> GetElementsAsync(CssSelector selector, CancellationToken cancellationToken)
        => GetElementsAsync((ElementSelector)selector, cancellationToken);

    public ValueTask<IEnumerable<IElement>> GetElementsAsync(CssSelector selector)
        => GetElementsAsync(selector, CancellationToken.None);

    public ValueTask<IEnumerable<IElement>> GetElementsAsync(ElementSelector selector, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ThrowIfDisposed();
        return MainFrame.GetElementsAsync(selector, cancellationToken);
    }

    public ValueTask<IEnumerable<IElement>> GetElementsAsync(ElementSelector selector)
        => GetElementsAsync(selector, CancellationToken.None);

    public ValueTask<IShadowRoot?> GetShadowRootAsync(string selector, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ThrowIfDisposed();
        return MainFrame.GetShadowRootAsync(selector, cancellationToken);
    }

    public ValueTask<IShadowRoot?> GetShadowRootAsync(string selector)
        => GetShadowRootAsync(selector, CancellationToken.None);

    public ValueTask<Memory<byte>> GetScreenshotAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return GetScreenshotCoreAsync(cancellationToken);
    }

    public ValueTask<Memory<byte>> GetScreenshotAsync()
        => GetScreenshotAsync(CancellationToken.None);

    private async ValueTask<Memory<byte>> GetScreenshotCoreAsync(CancellationToken cancellationToken)
    {
        if (BridgeCommands is { } bridge)
        {
            await OwnerWindow.ActivateAsync(cancellationToken).ConfigureAwait(false);
            var bridgeScreenshot = await TryGetBridgeScreenshotAsync(bridge, cancellationToken).ConfigureAwait(false);
            if (!bridgeScreenshot.IsEmpty)
                return bridgeScreenshot;
        }

        return await MainFrame.GetScreenshotAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<Memory<byte>> TryGetBridgeScreenshotAsync(PageBridgeCommandClient bridge, CancellationToken cancellationToken)
    {
        string? firstBridgeFailureMessage = null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var screenshot = await bridge.CaptureScreenshotAsync(cancellationToken).ConfigureAwait(false);
                var base64Payload = NormalizeBase64ScreenshotPayload(screenshot);
                if (!string.IsNullOrWhiteSpace(base64Payload))
                    return Convert.FromBase64String(base64Payload);

                return Memory<byte>.Empty;
            }
            catch (InvalidOperationException error)
            {
                if (attempt != 0)
                    throw CreateScreenshotRetryFailure(firstBridgeFailureMessage ?? error.Message, error.Message, error);

                firstBridgeFailureMessage = error.Message;
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);

                try
                {
                    await OwnerWindow.ActivateAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException retryError)
                {
                    throw CreateScreenshotRetryFailure(firstBridgeFailureMessage, retryError.Message, retryError);
                }
            }
        }

        return Memory<byte>.Empty;
    }

    private static InvalidOperationException CreateScreenshotRetryFailure(string firstFailureMessage, string retryFailureMessage, Exception retryFailure)
        => new($"Не удалось получить скриншот страницы после повторной попытки. Первая ошибка: {firstFailureMessage}. Ошибка повтора: {retryFailureMessage}", retryFailure);

    private static string? NormalizeBase64ScreenshotPayload(string? screenshot)
    {
        if (string.IsNullOrWhiteSpace(screenshot))
            return null;

        var trimmed = screenshot.Trim();
        if (!trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        var commaIndex = trimmed.IndexOf(',');
        return commaIndex >= 0 && commaIndex + 1 < trimmed.Length
            ? trimmed[(commaIndex + 1)..]
            : null;
    }

    internal async ValueTask<Point> ResolveViewportToScreenAsync(float viewportX, float viewportY, CancellationToken cancellationToken)
    {
        var nativeWindowPoint = await TryResolveViewportToScreenFromNativeWindowBoundsAsync(viewportX, viewportY, cancellationToken).ConfigureAwait(false);
        if (nativeWindowPoint is { } nativePoint)
            return nativePoint;

        var screenPoint = await EvaluateAsync(BuildResolveViewportToScreenScript(viewportX, viewportY), cancellationToken).ConfigureAwait(false);

        try
        {
            return ParseViewportToScreenPoint(screenPoint);
        }
        catch (InvalidOperationException)
        {
            var fallbackPoint = await TryResolveViewportToScreenFromWindowBoundsAsync(viewportX, viewportY, cancellationToken).ConfigureAwait(false);
            if (fallbackPoint is { } point)
                return point;

            throw;
        }
    }

    private async ValueTask<Point?> TryResolveViewportToScreenFromNativeWindowBoundsAsync(float viewportX, float viewportY, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            return null;

        Size? expectedWindowSize = null;
        if (BridgeCommands is { } bridge)
        {
            expectedWindowSize = (await bridge.GetWindowBoundsAsync(cancellationToken).ConfigureAwait(false)).Size;
        }
        else if (!OwnerWindow.ResolvedWindowSize.IsEmpty)
        {
            expectedWindowSize = OwnerWindow.ResolvedWindowSize;
        }

        var title = await GetTitleAsync(cancellationToken).ConfigureAwait(false);
        if (OwnerWindow.OwnerBrowser.TryGetLinuxNativeWindowBounds(expectedWindowSize, title) is not Rectangle nativeBounds)
            return null;

        return await ResolveViewportToScreenFromBoundsAsync(nativeBounds, viewportX, viewportY, cancellationToken).ConfigureAwait(false);
    }

    private const string ResolveViewportToScreenScriptTemplate = """
        (() => {
            const vx = __VIEWPORT_X__;
            const vy = __VIEWPORT_Y__;
            const documentWidth = Number(document.documentElement?.clientWidth ?? 0);
            const documentHeight = Number(document.documentElement?.clientHeight ?? 0);
            const bodyWidth = Number(document.body?.clientWidth ?? 0);
            const bodyHeight = Number(document.body?.clientHeight ?? 0);
            const visualViewportWidth = Number(window.visualViewport?.width ?? 0);
            const visualViewportHeight = Number(window.visualViewport?.height ?? 0);
            const outerWidth = Number(window.outerWidth), outerHeight = Number(window.outerHeight);
            const innerWidth = Number(window.innerWidth), innerHeight = Number(window.innerHeight);
            const screenLeft = Number(window.screenX), screenTop = Number(window.screenY);
            const mozInnerScreenLeft = typeof window.mozInnerScreenX === 'number' ? window.mozInnerScreenX : Number.NaN;
            const mozInnerScreenTop = typeof window.mozInnerScreenY === 'number' ? window.mozInnerScreenY : Number.NaN;
            const viewportWidth = Math.max(
                Number.isFinite(innerWidth) ? innerWidth : 0,
                Number.isFinite(documentWidth) ? documentWidth : 0,
                Number.isFinite(bodyWidth) ? bodyWidth : 0,
                Number.isFinite(visualViewportWidth) ? visualViewportWidth : 0);
            const viewportHeight = Math.max(
                Number.isFinite(innerHeight) ? innerHeight : 0,
                Number.isFinite(documentHeight) ? documentHeight : 0,
                Number.isFinite(bodyHeight) ? bodyHeight : 0,
                Number.isFinite(visualViewportHeight) ? visualViewportHeight : 0);
            const chromeLeft = Number.isFinite(outerWidth) && Number.isFinite(viewportWidth) && viewportWidth > 0
                ? Math.max(0, (outerWidth - viewportWidth) / 2)
                : 0;
            const chromeTop = Number.isFinite(outerHeight) && Number.isFinite(viewportHeight) && viewportHeight > 0
                ? Math.max(0, outerHeight - viewportHeight)
                : 0;
            const contentLeft = Number.isFinite(mozInnerScreenLeft)
                ? mozInnerScreenLeft
                : Number.isFinite(screenLeft)
                    ? screenLeft
                    : chromeLeft;
            const contentTop = Number.isFinite(mozInnerScreenTop)
                ? mozInnerScreenTop
                : Number.isFinite(screenTop)
                    ? screenTop
                    : chromeTop;
            const resolvedScreenX = contentLeft + vx;
            const resolvedScreenY = contentTop + vy;
            return JSON.stringify({
                screenX: Number.isFinite(resolvedScreenX) ? resolvedScreenX : (Number.isFinite(vx) ? vx : null),
                screenY: Number.isFinite(resolvedScreenY) ? resolvedScreenY : (Number.isFinite(vy) ? vy : null),
                debug: {
                    outerWidth: Number.isFinite(outerWidth) ? outerWidth : null,
                    outerHeight: Number.isFinite(outerHeight) ? outerHeight : null,
                    innerWidth: Number.isFinite(innerWidth) ? innerWidth : null,
                    innerHeight: Number.isFinite(innerHeight) ? innerHeight : null,
                    documentWidth: Number.isFinite(documentWidth) ? documentWidth : null,
                    documentHeight: Number.isFinite(documentHeight) ? documentHeight : null,
                    bodyWidth: Number.isFinite(bodyWidth) ? bodyWidth : null,
                    bodyHeight: Number.isFinite(bodyHeight) ? bodyHeight : null,
                    visualViewportWidth: Number.isFinite(visualViewportWidth) ? visualViewportWidth : null,
                    visualViewportHeight: Number.isFinite(visualViewportHeight) ? visualViewportHeight : null,
                    viewportWidth: Number.isFinite(viewportWidth) ? viewportWidth : null,
                    viewportHeight: Number.isFinite(viewportHeight) ? viewportHeight : null,
                    screenLeft: Number.isFinite(screenLeft) ? screenLeft : null,
                    screenTop: Number.isFinite(screenTop) ? screenTop : null,
                    mozInnerScreenLeft: Number.isFinite(mozInnerScreenLeft) ? mozInnerScreenLeft : null,
                    mozInnerScreenTop: Number.isFinite(mozInnerScreenTop) ? mozInnerScreenTop : null,
                    chromeLeft,
                    chromeTop,
                    contentLeft: Number.isFinite(contentLeft) ? contentLeft : null,
                    contentTop: Number.isFinite(contentTop) ? contentTop : null,
                    viewportX: Number.isFinite(vx) ? vx : null,
                    viewportY: Number.isFinite(vy) ? vy : null,
                    resolvedScreenX: Number.isFinite(resolvedScreenX) ? resolvedScreenX : null,
                    resolvedScreenY: Number.isFinite(resolvedScreenY) ? resolvedScreenY : null,
                }
            });
        })()
        """;

    private static string BuildResolveViewportToScreenScript(float viewportX, float viewportY)
        => ResolveViewportToScreenScriptTemplate
            .Replace("__VIEWPORT_X__", viewportX.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__VIEWPORT_Y__", viewportY.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static Point ParseViewportToScreenPoint(JsonElement? rawScreenPoint)
    {
        JsonElement pointElement;
        if (rawScreenPoint is not { } payload)
            throw new InvalidOperationException("Не удалось преобразовать viewport-координаты элемента в экранные координаты: payload=<null>");

        if (payload.ValueKind == JsonValueKind.String)
        {
            var json = payload.GetString();
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("Не удалось преобразовать viewport-координаты элемента в экранные координаты: payload=<empty-string>");

            using var pointDocument = JsonDocument.Parse(json);
            pointElement = pointDocument.RootElement.Clone();
        }
        else
        {
            pointElement = payload;
        }

        if (pointElement.ValueKind != JsonValueKind.Object
            || !pointElement.TryGetProperty("screenX", out var screenX)
            || !pointElement.TryGetProperty("screenY", out var screenY)
            || !screenX.TryGetDouble(out var resolvedScreenX)
            || !screenY.TryGetDouble(out var resolvedScreenY))
        {
            throw new InvalidOperationException($"Не удалось преобразовать viewport-координаты элемента в экранные координаты: payload={DescribeViewportToScreenPayload(pointElement)}");
        }

        return new Point((int)Math.Round(resolvedScreenX), (int)Math.Round(resolvedScreenY));
    }

    private static string DescribeViewportToScreenPayload(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Undefined)
            return "<undefined>";

        if (payload.ValueKind == JsonValueKind.String)
            return payload.GetString() ?? "<null-string>";

        return payload.GetRawText();
    }

    private async ValueTask<Point?> TryResolveViewportToScreenFromWindowBoundsAsync(float viewportX, float viewportY, CancellationToken cancellationToken)
    {
        if (await OwnerWindow.GetBoundingBoxAsync(cancellationToken).ConfigureAwait(false) is not Rectangle bounds)
            return null;

        return await ResolveViewportToScreenFromBoundsAsync(bounds, viewportX, viewportY, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<Point?> ResolveViewportToScreenFromBoundsAsync(Rectangle bounds, float viewportX, float viewportY, CancellationToken cancellationToken)
    {
        var viewportSize = await GetViewportSizeAsync(cancellationToken).ConfigureAwait(false);
        var chromeLeft = viewportSize is { Width: > 0 }
            ? Math.Max(0, (bounds.Width - viewportSize.Value.Width) / 2f)
            : 0f;
        var chromeTop = viewportSize is { Height: > 0 }
            ? Math.Max(0, bounds.Height - viewportSize.Value.Height)
            : 0f;

        return new Point(
            (int)Math.Round(bounds.X + chromeLeft + viewportX),
            (int)Math.Round(bounds.Y + chromeTop + viewportY));
    }

    public ValueTask<bool> IsVisibleAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return MainFrame.IsVisibleAsync(cancellationToken);
    }

    public ValueTask<bool> IsVisibleAsync()
        => IsVisibleAsync(CancellationToken.None);

    public async ValueTask<Size?> GetViewportSizeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var result = await EvaluateAsync(
            "JSON.stringify({ width: Math.max(window.innerWidth || 0, document.documentElement?.clientWidth || 0, document.body?.clientWidth || 0, window.visualViewport?.width || 0), height: Math.max(window.innerHeight || 0, document.documentElement?.clientHeight || 0, document.body?.clientHeight || 0, window.visualViewport?.height || 0) })",
            cancellationToken).ConfigureAwait(false);
        JsonElement element;
        JsonDocument? document = null;

        try
        {
            if (result is not JsonElement jsonElement)
            {
                element = default;
            }
            else if (jsonElement.ValueKind == JsonValueKind.String)
            {
                var json = jsonElement.GetString();
                if (!string.IsNullOrWhiteSpace(json))
                {
                    document = JsonDocument.Parse(json);
                    element = document.RootElement;
                }
                else
                {
                    element = default;
                }
            }
            else
            {
                element = jsonElement;
            }

            if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("width", out var width)
            && element.TryGetProperty("height", out var height)
            && width.TryGetInt32(out var viewportWidth)
            && height.TryGetInt32(out var viewportHeight))
            {
                return new Size(viewportWidth, viewportHeight);
            }
        }
        finally
        {
            document?.Dispose();
        }

        if (ResolvedDevice is { ViewportSize.IsEmpty: false } device)
            return device.ViewportSize;

        if (!OwnerWindow.ResolvedWindowSize.IsEmpty)
            return OwnerWindow.ResolvedWindowSize;

        return null;
    }

    public ValueTask<Size?> GetViewportSizeAsync()
        => GetViewportSizeAsync(CancellationToken.None);
}