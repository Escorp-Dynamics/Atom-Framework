using System.Drawing;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Hardware.Input;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет runtime-реализацию DOM-элемента и операций взаимодействия с ним.
/// </summary>
public sealed class Element : IElement
{
    private const string CallbackBridgeEventName = "atom-webdriver-callback";
    private const string ElementCallbackRegistryKey = "__atomWebDriverElementCallbacks";
    private readonly string handle;
    private readonly HtmlFallbackElementState? fallbackState;
    private readonly Dictionary<(string EventName, Delegate Handler), ElementEventListenerRegistration> eventListenerRegistrations = [];
    private readonly Lock eventListenerGate = new();
    private IFrame? contentFrame;

    private void ThrowIfDisposed() => OwnerPage.ThrowIfDisposed();

    internal Element(WebPage page, IFrame? frame = null, string? bridgeElementId = null, HtmlFallbackElementState? fallbackState = null)
    {
        Page = page;
        Frame = frame ?? page.MainFrame;
        BridgeElementId = string.IsNullOrWhiteSpace(bridgeElementId) ? null : bridgeElementId;
        this.fallbackState = fallbackState;
        handle = BridgeElementId ?? fallbackState?.TryGetAttribute("id") ?? Guid.NewGuid().ToString("N");
    }

    internal Element(Frame frame, string? bridgeElementId = null, HtmlFallbackElementState? fallbackState = null)
        : this((WebPage)frame.Page, frame, bridgeElementId, fallbackState)
    {
    }

    public IWebPage Page { get; }

    public bool IsDisposed => OwnerPage.IsDisposed;

    public IFrame Frame { get; }

    internal string? BridgeElementId { get; }

    private WebPage OwnerPage => (WebPage)Page;

    public ValueTask ClickAsync(CancellationToken cancellationToken)
        => ClickAsync(new ClickSettings(), cancellationToken);

    public ValueTask ClickAsync() => ClickAsync(CancellationToken.None);

    public async ValueTask ClickAsync(ClickSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        OwnerPage.OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebElementClickStarting(handle, OwnerPage.TabId);

        await OwnerPage.OwnerWindow.ActivateAsync(cancellationToken).ConfigureAwait(false);
        var mouse = await OwnerPage.ResolveMouseAsync(cancellationToken).ConfigureAwait(false);
        var interactionPoint = await ResolveInteractionPointAsync(cancellationToken).ConfigureAwait(false);
        interactionPoint = await CalibrateInteractionPointAsync(mouse, interactionPoint, cancellationToken).ConfigureAwait(false);
        await mouse.ClickAtAsync(interactionPoint, settings.Button, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask ClickAsync(ClickSettings settings) => ClickAsync(settings, CancellationToken.None);

    public async ValueTask HoverAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        OwnerPage.OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebElementHoverStarting(handle, OwnerPage.TabId);

        await OwnerPage.OwnerWindow.ActivateAsync(cancellationToken).ConfigureAwait(false);
        var mouse = await OwnerPage.ResolveMouseAsync(cancellationToken).ConfigureAwait(false);
        await ApproachInteractionPointAsync(mouse, await ResolveInteractionPointAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
    }

    public ValueTask HoverAsync() => HoverAsync(CancellationToken.None);

    private static async ValueTask ApproachInteractionPointAsync(VirtualMouse mouse, Point interactionPoint, CancellationToken cancellationToken)
    {
        var diagonalApproachPoint = new Point(Math.Max(0, interactionPoint.X - 240), Math.Max(0, interactionPoint.Y - 240));
        var horizontalApproachPoint = new Point(Math.Max(0, interactionPoint.X - 160), interactionPoint.Y);

        mouse.MoveAbsolute(diagonalApproachPoint);
        await Task.Delay(35, cancellationToken).ConfigureAwait(false);
        mouse.MoveAbsolute(horizontalApproachPoint);
        await Task.Delay(35, cancellationToken).ConfigureAwait(false);
        mouse.MoveAbsolute(interactionPoint);
        await Task.Delay(90, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<Point> CalibrateInteractionPointAsync(VirtualMouse mouse, Point interactionPoint, CancellationToken cancellationToken)
    {
        var calibrationState = await ResetPointerCalibrationStateAsync(cancellationToken).ConfigureAwait(false);
        if (calibrationState is null)
        {
            await ApproachInteractionPointAsync(mouse, interactionPoint, cancellationToken).ConfigureAwait(false);
            return interactionPoint;
        }

        await ApproachInteractionPointAsync(mouse, interactionPoint, cancellationToken).ConfigureAwait(false);

        calibrationState = await ReadPointerCalibrationStateAsync(cancellationToken).ConfigureAwait(false);
        if (calibrationState is not { } resolvedCalibrationState || resolvedCalibrationState.IsHovered)
            return interactionPoint;

        if (!resolvedCalibrationState.TargetCenterX.HasValue
            || !resolvedCalibrationState.TargetCenterY.HasValue
            || !resolvedCalibrationState.LastClientX.HasValue
            || !resolvedCalibrationState.LastClientY.HasValue)
        {
            return interactionPoint;
        }

        var correctedInteractionPoint = new Point(
            (int)Math.Round(interactionPoint.X + (resolvedCalibrationState.TargetCenterX.Value - resolvedCalibrationState.LastClientX.Value)),
            (int)Math.Round(interactionPoint.Y + (resolvedCalibrationState.TargetCenterY.Value - resolvedCalibrationState.LastClientY.Value)));

        if (correctedInteractionPoint == interactionPoint)
            return interactionPoint;

        mouse.MoveAbsolute(correctedInteractionPoint);
        await Task.Delay(90, cancellationToken).ConfigureAwait(false);

        return correctedInteractionPoint;
    }

    public ValueTask ScrollIntoViewAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (OwnerPage.BridgeCommands is { } bridge && BridgeElementId is { } elementId)
            return bridge.ScrollElementIntoViewAsync(elementId, cancellationToken);

        return ValueTask.CompletedTask;
    }

    public ValueTask ScrollIntoViewAsync() => ScrollIntoViewAsync(CancellationToken.None);

    public async ValueTask FocusAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        OwnerPage.OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebElementFocusStarting(handle, OwnerPage.TabId);

        if (OwnerPage.BridgeCommands is { } bridge && BridgeElementId is { } elementId)
        {
            await bridge.FocusElementAsync(elementId, scrollIntoView: true, cancellationToken).ConfigureAwait(false);
            return;
        }

        await OwnerPage.OwnerWindow.ActivateAsync(cancellationToken).ConfigureAwait(false);
        var mouse = await OwnerPage.ResolveMouseAsync(cancellationToken).ConfigureAwait(false);
        var interactionPoint = await ResolveInteractionPointAsync(cancellationToken).ConfigureAwait(false);
        await ApproachInteractionPointAsync(mouse, interactionPoint, cancellationToken).ConfigureAwait(false);
        await mouse.ClickAtAsync(interactionPoint, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public ValueTask FocusAsync() => FocusAsync(CancellationToken.None);

    private async ValueTask PrepareForTrustedKeyboardInputAsync(bool requireDocumentFocus, CancellationToken cancellationToken)
    {
        await OwnerPage.OwnerWindow.ActivateAsync(cancellationToken).ConfigureAwait(false);

        if (OwnerPage.BridgeCommands is { } bridge && BridgeElementId is { } elementId)
        {
            await bridge.FocusElementAsync(elementId, scrollIntoView: true, cancellationToken).ConfigureAwait(false);

            try
            {
                var mouse = await OwnerPage.ResolveMouseAsync(cancellationToken).ConfigureAwait(false);
                var interactionPoint = await ResolveInteractionPointAsync(cancellationToken).ConfigureAwait(false);
                await ApproachInteractionPointAsync(mouse, interactionPoint, cancellationToken).ConfigureAwait(false);

                if (requireDocumentFocus && !await IsTrustedKeyboardTargetReadyAsync(cancellationToken).ConfigureAwait(false))
                    await ClickAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (VirtualMouseException)
            {
                return;
            }

            return;
        }

        await FocusAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<bool> IsTrustedKeyboardTargetReadyAsync(CancellationToken cancellationToken)
        => await EvaluateAsync<bool>("document.hasFocus() && (document.activeElement === element || (element instanceof HTMLElement && element.matches(':focus')))", cancellationToken).ConfigureAwait(false);

    public async ValueTask TypeAsync(string text, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(text);
        OwnerPage.OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebElementTypeStarting(handle, OwnerPage.TabId, text.Length);

        await PrepareForTrustedKeyboardInputAsync(requireDocumentFocus: true, cancellationToken).ConfigureAwait(false);
        var keyboard = await OwnerPage.ResolveKeyboardAsync(cancellationToken).ConfigureAwait(false);

        foreach (var character in text)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryMapCharacterToKey(character, out var key, out var modifiers))
                throw new NotSupportedException($"Символ '{character}' пока не поддерживается на пути доверенного ввода с клавиатуры");

            if (modifiers == default)
                await keyboard.KeyPressAsync(key, cancellationToken).ConfigureAwait(false);
            else
                await keyboard.KeyPressAsync(key, modifiers, cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask TypeAsync(string text) => TypeAsync(text, CancellationToken.None);

    public async ValueTask PressAsync(ConsoleKey key, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        OwnerPage.OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebElementPressStarting(handle, OwnerPage.TabId, key.ToString());

        await PrepareForTrustedKeyboardInputAsync(requireDocumentFocus: false, cancellationToken).ConfigureAwait(false);
        var keyboard = await OwnerPage.ResolveKeyboardAsync(cancellationToken).ConfigureAwait(false);
        await keyboard.KeyPressAsync(key, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask PressAsync(ConsoleKey key) => PressAsync(key, CancellationToken.None);

    public ValueTask HumanityClickAsync(CancellationToken cancellationToken)
        => ClickAsync(cancellationToken);

    public ValueTask HumanityClickAsync() => HumanityClickAsync(CancellationToken.None);

    public ValueTask HumanityTypeAsync(string text, CancellationToken cancellationToken)
        => TypeAsync(text, cancellationToken);

    public ValueTask HumanityTypeAsync(string text) => HumanityTypeAsync(text, CancellationToken.None);
    public async ValueTask<string?> GetInnerTextAsync(CancellationToken cancellationToken)
        => await GetBridgePropertyOrFallbackAsync("innerText", static state => state.InnerText, cancellationToken).ConfigureAwait(false);
    public ValueTask<string?> GetInnerTextAsync() => GetInnerTextAsync(CancellationToken.None);
    public async ValueTask<string?> GetInnerHtmlAsync(CancellationToken cancellationToken)
        => await GetBridgePropertyOrFallbackAsync("innerHTML", static state => state.InnerHtml, cancellationToken).ConfigureAwait(false);
    public ValueTask<string?> GetInnerHtmlAsync() => GetInnerHtmlAsync(CancellationToken.None);
    public async ValueTask<string?> GetValueAsync(CancellationToken cancellationToken)
        => await GetBridgePropertyOrFallbackAsync("value", static state => state.TryGetAttribute("value"), cancellationToken).ConfigureAwait(false);
    public ValueTask<string?> GetValueAsync() => GetValueAsync(CancellationToken.None);
    public async ValueTask<string?> GetAttributeAsync(string attributeName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (OwnerPage.BridgeCommands is { } bridge && BridgeElementId is { } elementId)
            return await bridge.GetElementPropertyAsync(elementId, attributeName, cancellationToken).ConfigureAwait(false);

        return GetFallbackState()?.TryGetAttribute(attributeName);
    }
    public ValueTask<string?> GetAttributeAsync(string attributeName) => GetAttributeAsync(attributeName, CancellationToken.None);
    public async ValueTask<bool> IsVisibleAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var description = await TryDescribeBridgeElementAsync(cancellationToken).ConfigureAwait(false);
        if (description is not null)
            return description.IsVisible;

        return await Frame.IsVisibleAsync(cancellationToken).ConfigureAwait(false);
    }
    public ValueTask<bool> IsVisibleAsync() => IsVisibleAsync(CancellationToken.None);
    public async ValueTask<Rectangle?> GetBoundingBoxAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var description = await TryDescribeBridgeElementAsync(cancellationToken).ConfigureAwait(false);
        if (description is not null)
            return Rectangle.Round(description.BoundingBox);

        return await Frame.GetBoundingBoxAsync(cancellationToken).ConfigureAwait(false);
    }
    public ValueTask<Rectangle?> GetBoundingBoxAsync() => GetBoundingBoxAsync(CancellationToken.None);
    public async ValueTask<IReadOnlyDictionary<string, string>> GetComputedStyleAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var description = await TryDescribeBridgeElementAsync(cancellationToken).ConfigureAwait(false);
        if (description is not null)
            return description.ComputedStyle;

        return new Dictionary<string, string>(StringComparer.Ordinal);
    }
    public ValueTask<IReadOnlyDictionary<string, string>> GetComputedStyleAsync() => GetComputedStyleAsync(CancellationToken.None);
    public async ValueTask<bool> IsCheckedAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var description = await TryDescribeBridgeElementAsync(cancellationToken).ConfigureAwait(false);
        if (description is not null)
            return description.Checked;

        return GetFallbackState()?.HasBooleanAttribute("checked") == true;
    }
    public ValueTask<bool> IsCheckedAsync() => IsCheckedAsync(CancellationToken.None);
    public ValueTask<bool> IsDisabledAsync(CancellationToken cancellationToken)
        => GetBridgeBooleanPropertyOrFallbackAsync("disabled", static state => state.HasBooleanAttribute("disabled"), cancellationToken);
    public ValueTask<bool> IsDisabledAsync() => IsDisabledAsync(CancellationToken.None);
    public async ValueTask<IEnumerable<string>> GetClassListAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (OwnerPage.BridgeCommands is { } bridge && BridgeElementId is { } elementId)
        {
            var className = await bridge.GetElementPropertyAsync(elementId, "className", cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(className)
                ? []
                : className.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return GetFallbackState()?.ClassList ?? [];
    }
    public ValueTask<IEnumerable<string>> GetClassListAsync() => GetClassListAsync(CancellationToken.None);
    public ValueTask<Memory<byte>> GetScreenshotAsync(CancellationToken cancellationToken)
        => GetScreenshotCoreAsync(cancellationToken);

    private async ValueTask<Memory<byte>> GetScreenshotCoreAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var frameScreenshot = await Frame.GetScreenshotAsync(cancellationToken).ConfigureAwait(false);
        if (frameScreenshot.IsEmpty)
            return Memory<byte>.Empty;

        var bounds = await GetBoundingBoxAsync(cancellationToken).ConfigureAwait(false);
        return bounds is Rectangle rectangle
            ? ScreenshotCropper.CropPng(frameScreenshot, rectangle)
            : Memory<byte>.Empty;
    }

    public ValueTask<Memory<byte>> GetScreenshotAsync() => GetScreenshotAsync(CancellationToken.None);
    public ValueTask<bool> IsIntersectingViewportAsync(CancellationToken cancellationToken) => IsVisibleAsync(cancellationToken);
    public ValueTask<bool> IsIntersectingViewportAsync() => IsIntersectingViewportAsync(CancellationToken.None);
    public ValueTask<string?> GetElementHandleAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>(BridgeElementId ?? handle);
    }
    public ValueTask<string?> GetElementHandleAsync() => GetElementHandleAsync(CancellationToken.None);
    public ValueTask<string?> GetIdAsync(CancellationToken cancellationToken)
        => GetAttributeAsync("id", cancellationToken);
    public ValueTask<string?> GetIdAsync() => GetIdAsync(CancellationToken.None);
    public async ValueTask<string?> GetPropertyAsync(string propertyName, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        cancellationToken.ThrowIfCancellationRequested();

        if (OwnerPage.BridgeCommands is { } bridge && BridgeElementId is { } elementId)
            return await bridge.GetElementPropertyAsync(elementId, propertyName, cancellationToken).ConfigureAwait(false);

        return propertyName switch
        {
            "innerHTML" => await GetInnerHtmlAsync(cancellationToken).ConfigureAwait(false),
            "innerText" or "textContent" => await GetInnerTextAsync(cancellationToken).ConfigureAwait(false),
            "id" => await GetIdAsync(cancellationToken).ConfigureAwait(false),
            "value" => await GetValueAsync(cancellationToken).ConfigureAwait(false),
            _ => await GetAttributeAsync(propertyName, cancellationToken).ConfigureAwait(false),
        };
    }
    public ValueTask<string?> GetPropertyAsync(string propertyName) => GetPropertyAsync(propertyName, CancellationToken.None);
    public ValueTask<bool> IsContentEditableAsync(CancellationToken cancellationToken)
        => GetBridgeBooleanPropertyOrFallbackAsync("isContentEditable", static state => state.GetBooleanAttributeValue("contenteditable"), cancellationToken);
    public ValueTask<bool> IsContentEditableAsync() => IsContentEditableAsync(CancellationToken.None);
    public ValueTask<bool> IsDraggableAsync(CancellationToken cancellationToken)
        => GetBridgeBooleanPropertyOrFallbackAsync("draggable", static state => state.GetBooleanAttributeValue("draggable"), cancellationToken);
    public ValueTask<bool> IsDraggableAsync() => IsDraggableAsync(CancellationToken.None);
    public ValueTask<string?> GetAriaLabelAsync(CancellationToken cancellationToken)
        => GetAttributeAsync("aria-label", cancellationToken);
    public ValueTask<string?> GetAriaLabelAsync() => GetAriaLabelAsync(CancellationToken.None);
    public ValueTask<string?> GetRoleAsync(CancellationToken cancellationToken)
        => GetAttributeAsync("role", cancellationToken);
    public ValueTask<string?> GetRoleAsync() => GetRoleAsync(CancellationToken.None);
    public async ValueTask<int?> GetTabIndexAsync(CancellationToken cancellationToken)
    {
        var value = await GetAttributeAsync("tabindex", cancellationToken).ConfigureAwait(false);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tabIndex) ? tabIndex : null;
    }
    public ValueTask<int?> GetTabIndexAsync() => GetTabIndexAsync(CancellationToken.None);
    public async ValueTask<bool> IsFocusedAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var description = await TryDescribeBridgeElementAsync(cancellationToken).ConfigureAwait(false);
        return description?.IsActive == true;
    }
    public ValueTask<bool> IsFocusedAsync() => IsFocusedAsync(CancellationToken.None);
    public async ValueTask<bool> IsEditableAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var tagName = await GetBridgePropertyOrFallbackAsync("tagName", static state => state.TagName, cancellationToken).ConfigureAwait(false);
        var isEditableTag = tagName is not null
            && (tagName.Equals("input", StringComparison.OrdinalIgnoreCase)
                || tagName.Equals("textarea", StringComparison.OrdinalIgnoreCase));

        if (!isEditableTag && !await IsContentEditableAsync(cancellationToken).ConfigureAwait(false))
            return false;

        return !await IsDisabledAsync(cancellationToken).ConfigureAwait(false);
    }
    public ValueTask<bool> IsEditableAsync() => IsEditableAsync(CancellationToken.None);
    public ValueTask<bool> IsSelectedAsync(CancellationToken cancellationToken)
        => GetBridgeBooleanPropertyOrFallbackAsync("selected", static state => state.HasBooleanAttribute("selected"), cancellationToken);
    public ValueTask<bool> IsSelectedAsync() => IsSelectedAsync(CancellationToken.None);
    public ValueTask<IElement?> GetParentElementAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        return GetParentElementCoreAsync(cancellationToken);
    }
    public ValueTask<IElement?> GetParentElementAsync() => GetParentElementAsync(CancellationToken.None);
    public ValueTask<IEnumerable<IElement>> GetChildElementsAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        return GetChildElementsCoreAsync(cancellationToken);
    }
    public ValueTask<IEnumerable<IElement>> GetChildElementsAsync() => GetChildElementsAsync(CancellationToken.None);
    public ValueTask<IEnumerable<IElement>> GetSiblingElementsAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        return GetSiblingElementsCoreAsync(cancellationToken);
    }
    public ValueTask<IEnumerable<IElement>> GetSiblingElementsAsync() => GetSiblingElementsAsync(CancellationToken.None);
    public ValueTask<string?> GetElementPathAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        return GetElementPathCoreAsync(cancellationToken);
    }
    public ValueTask<string?> GetElementPathAsync() => GetElementPathAsync(CancellationToken.None);
    public ValueTask<string?> GetCustomDataAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return GetAttributeAsync($"data-{key}", cancellationToken);
    }
    public ValueTask<string?> GetCustomDataAsync(string key) => GetCustomDataAsync(key, CancellationToken.None);
    public ValueTask<bool> IsAnimatingAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
    public ValueTask<bool> IsAnimatingAsync() => IsAnimatingAsync(CancellationToken.None);
    public ValueTask<string?> GetAnimationStateAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>(null);
    }
    public ValueTask<string?> GetAnimationStateAsync() => GetAnimationStateAsync(CancellationToken.None);
    public ValueTask<bool> IsOverflowingAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
    public ValueTask<bool> IsOverflowingAsync() => IsOverflowingAsync(CancellationToken.None);
    public ValueTask<string?> GetOverflowDirectionAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>(null);
    }
    public ValueTask<string?> GetOverflowDirectionAsync() => GetOverflowDirectionAsync(CancellationToken.None);
    public ValueTask<bool> IsClippedAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
    public ValueTask<bool> IsClippedAsync() => IsClippedAsync(CancellationToken.None);
    public ValueTask<string?> GetClipPathAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>(null);
    }
    public ValueTask<string?> GetClipPathAsync() => GetClipPathAsync(CancellationToken.None);
    public ValueTask<bool> IsPointerOverAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
    public ValueTask<bool> IsPointerOverAsync() => IsPointerOverAsync(CancellationToken.None);
    public ValueTask<Point?> GetPointerCoordinatesAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<Point?>(null);
    }
    public ValueTask<Point?> GetPointerCoordinatesAsync() => GetPointerCoordinatesAsync(CancellationToken.None);
    public ValueTask<bool> IsPointerDownAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
    public ValueTask<bool> IsPointerDownAsync() => IsPointerDownAsync(CancellationToken.None);
    public ValueTask<VirtualMouseButton?> GetPointerButtonAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<VirtualMouseButton?>(null);
    }
    public ValueTask<VirtualMouseButton?> GetPointerButtonAsync() => GetPointerButtonAsync(CancellationToken.None);
    public ValueTask<bool> IsPointerDraggingAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
    public ValueTask<bool> IsPointerDraggingAsync() => IsPointerDraggingAsync(CancellationToken.None);
    public ValueTask<string?> GetDragDataAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>(null);
    }
    public ValueTask<string?> GetDragDataAsync() => GetDragDataAsync(CancellationToken.None);
    public ValueTask<bool> IsDroppingAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
    public ValueTask<bool> IsDroppingAsync() => IsDroppingAsync(CancellationToken.None);
    public ValueTask<IElement?> GetDropTargetAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IElement?>(null);
    }
    public ValueTask<IElement?> GetDropTargetAsync() => GetDropTargetAsync(CancellationToken.None);
    public ValueTask<string?> GetDropEffectAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>(null);
    }
    public ValueTask<string?> GetDropEffectAsync() => GetDropEffectAsync(CancellationToken.None);
    public ValueTask<bool> IsContentOverflowingAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
    public ValueTask<bool> IsContentOverflowingAsync() => IsContentOverflowingAsync(CancellationToken.None);
    public ValueTask<string?> GetContentOverflowDirectionAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>(null);
    }
    public ValueTask<string?> GetContentOverflowDirectionAsync() => GetContentOverflowDirectionAsync(CancellationToken.None);
    public ValueTask<bool> IsContentClippedAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(false);
    }
    public ValueTask<bool> IsContentClippedAsync() => IsContentClippedAsync(CancellationToken.None);
    public ValueTask<string?> GetContentClipPathAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<string?>(null);
    }
    public ValueTask<string?> GetContentClipPathAsync() => GetContentClipPathAsync(CancellationToken.None);
    public ValueTask SetValueAsync(string value, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(value);
        return ExecuteElementScriptAsync($$"""
const value = {{SerializeJavaScriptString(value)}};
if (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement || element instanceof HTMLSelectElement) {
    element.value = value;
} else {
    element.setAttribute('value', value);
    if ('value' in element) {
        try {
            element.value = value;
        } catch {
        }
    }
}

element.dispatchEvent(new Event('input', { bubbles: true }));
element.dispatchEvent(new Event('change', { bubbles: true }));
return true;
""", cancellationToken);
    }
    public ValueTask SetValueAsync(string value) => SetValueAsync(value, CancellationToken.None);
    public ValueTask SetAttributeAsync(string attributeName, string value, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        ArgumentNullException.ThrowIfNull(value);
        return ExecuteElementScriptAsync($$"""
    element.setAttribute({{SerializeJavaScriptString(attributeName)}}, {{SerializeJavaScriptString(value)}});
return true;
""", cancellationToken);
    }
    public ValueTask SetAttributeAsync(string attributeName, string value) => SetAttributeAsync(attributeName, value, CancellationToken.None);
    public ValueTask SetStyleAsync(string propertyName, string value, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(value);
        return ExecuteElementScriptAsync($$"""
    element.style.setProperty({{SerializeJavaScriptString(propertyName)}}, {{SerializeJavaScriptString(value)}});
return true;
""", cancellationToken);
    }
    public ValueTask SetStyleAsync(string propertyName, string value) => SetStyleAsync(propertyName, value, CancellationToken.None);
    public ValueTask AddClassAsync(string className, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        return ExecuteElementScriptAsync($$"""
    element.classList.add({{SerializeJavaScriptString(className)}});
return true;
""", cancellationToken);
    }
    public ValueTask AddClassAsync(string className) => AddClassAsync(className, CancellationToken.None);
    public ValueTask RemoveClassAsync(string className, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        return ExecuteElementScriptAsync($$"""
    element.classList.remove({{SerializeJavaScriptString(className)}});
return true;
""", cancellationToken);
    }
    public ValueTask RemoveClassAsync(string className) => RemoveClassAsync(className, CancellationToken.None);
    public ValueTask ToggleClassAsync(string className, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        return ExecuteElementScriptAsync($$"""
    element.classList.toggle({{SerializeJavaScriptString(className)}});
return true;
""", cancellationToken);
    }
    public ValueTask ToggleClassAsync(string className) => ToggleClassAsync(className, CancellationToken.None);
    public ValueTask SetContentAsync(string html, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(html);
        return ExecuteElementScriptAsync($$"""
    element.innerHTML = {{SerializeJavaScriptString(html)}};
return true;
""", cancellationToken);
    }
    public ValueTask SetContentAsync(string html) => SetContentAsync(html, CancellationToken.None);
    public ValueTask SetCustomPropertyAsync(string propertyName, string value, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(value);
        return ExecuteElementScriptAsync($$"""
    element[{{SerializeJavaScriptString(propertyName)}}] = {{SerializeJavaScriptString(value)}};
return true;
""", cancellationToken);
    }
    public ValueTask SetCustomPropertyAsync(string propertyName, string value) => SetCustomPropertyAsync(propertyName, value, CancellationToken.None);
    public ValueTask SetDataAsync(string key, string value, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        return ExecuteElementScriptAsync($$"""
    element.setAttribute('data-' + {{SerializeJavaScriptString(key)}}, {{SerializeJavaScriptString(value)}});
return true;
""", cancellationToken);
    }
    public ValueTask SetDataAsync(string key, string value) => SetDataAsync(key, value, CancellationToken.None);
    public async ValueTask AddEventListenerAsync(string eventName, Delegate handler, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(handler);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (OwnerPage.BridgeCommands is not { } || BridgeElementId is not { })
            throw SurfaceGuards.Unsupported(nameof(AddEventListenerAsync));

        lock (eventListenerGate)
        {
            if (eventListenerRegistrations.ContainsKey((eventName, handler)))
                return;
        }

        var callbackName = "atom.element.callback." + Guid.NewGuid().ToString("N");
        var listenerId = "atom-element-listener-" + Guid.NewGuid().ToString("N");

        AsyncEventHandler<IWebPage, CallbackEventArgs> callbackBridge = async (_, args) =>
        {
            if (!string.Equals(args.Name, callbackName, StringComparison.Ordinal))
                return;

            await InvokeElementEventHandlerAsync(handler, args.Args).ConfigureAwait(false);
        };

        await OwnerPage.SubscribeAsync(callbackName, cancellationToken).ConfigureAwait(false);
        OwnerPage.Callback += callbackBridge;

        try
        {
            await ExecuteElementScriptAsync(CreateAddEventListenerScript(eventName, listenerId, callbackName), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            OwnerPage.Callback -= callbackBridge;
            await OwnerPage.UnSubscribeAsync(callbackName, cancellationToken).ConfigureAwait(false);
            throw;
        }

        lock (eventListenerGate)
        {
            eventListenerRegistrations[(eventName, handler)] = new ElementEventListenerRegistration(listenerId, callbackName, callbackBridge);
        }
    }
    public ValueTask AddEventListenerAsync(string eventName, Delegate handler) => AddEventListenerAsync(eventName, handler, CancellationToken.None);
    public async ValueTask RemoveEventListenerAsync(string eventName, Delegate handler, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(handler);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        ElementEventListenerRegistration? registration;
        lock (eventListenerGate)
        {
            eventListenerRegistrations.TryGetValue((eventName, handler), out registration);
            if (registration is not null)
                eventListenerRegistrations.Remove((eventName, handler));
        }

        if (registration is null)
            return;

        OwnerPage.Callback -= registration.CallbackBridge;

        if (OwnerPage.BridgeCommands is { } && BridgeElementId is { })
            await ExecuteElementScriptAsync(CreateRemoveEventListenerScript(eventName, registration.ListenerId), cancellationToken).ConfigureAwait(false);

        await OwnerPage.UnSubscribeAsync(registration.CallbackName, cancellationToken).ConfigureAwait(false);
    }
    public ValueTask RemoveEventListenerAsync(string eventName, Delegate handler) => RemoveEventListenerAsync(eventName, handler, CancellationToken.None);
    public ValueTask<JsonElement?> EvaluateAsync(string script, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return EvaluateElementScriptCoreAsync(script, preferPageContextOnNull: true, forcePageContextExecution: false, cancellationToken);
    }
    public ValueTask<JsonElement?> EvaluateAsync(string script) => EvaluateAsync(script, CancellationToken.None);
    public async ValueTask<TResult?> EvaluateAsync<TResult>(string script, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var result = await EvaluateElementScriptCoreAsync(script, preferPageContextOnNull: true, forcePageContextExecution: false, cancellationToken).ConfigureAwait(false);
        if (result is not JsonElement element)
            return default;

        if (typeof(TResult) == typeof(string))
            return (TResult?)(object?)element.GetString();

        if (typeof(TResult) == typeof(bool) && (element.ValueKind is JsonValueKind.True or JsonValueKind.False))
            return (TResult?)(object)element.GetBoolean();

        if (typeof(TResult) == typeof(int) && element.TryGetInt32(out var intValue))
            return (TResult?)(object)intValue;

        if (typeof(TResult) == typeof(double) && element.TryGetDouble(out var doubleValue))
            return (TResult?)(object)doubleValue;

        if (typeof(TResult) == typeof(Uri) && element.ValueKind == JsonValueKind.String && Uri.TryCreate(element.GetString(), UriKind.Absolute, out var uri))
            return (TResult?)(object)uri;

        if (typeof(TResult) == typeof(JsonElement))
            return (TResult?)(object)element;

        return default;
    }
    public ValueTask<TResult?> EvaluateAsync<TResult>(string script) => EvaluateAsync<TResult>(script, CancellationToken.None);
    public ValueTask<IFrame?> GetFrameAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IFrame?>(Frame);
    }
    public ValueTask<IFrame?> GetFrameAsync() => GetFrameAsync(CancellationToken.None);
    public async ValueTask<IEnumerable<IFrame>> GetChildFramesAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (contentFrame is not null)
            return [contentFrame];

        if (BridgeElementId is null || Frame is not Frame ownerFrame)
            return [];

        var tagName = await GetPropertyAsync("tagName", cancellationToken).ConfigureAwait(false);
        if (!string.Equals(tagName, "IFRAME", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(tagName, "FRAME", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return [OwnerPage.GetOrCreateChildFrame(ownerFrame, this)];
    }
    public ValueTask<IEnumerable<IFrame>> GetChildFramesAsync() => GetChildFramesAsync(CancellationToken.None);
    public ValueTask<IFrame?> GetParentFrameAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return Frame.GetParentFrameAsync(cancellationToken);
    }
    public ValueTask<IFrame?> GetParentFrameAsync() => GetParentFrameAsync(CancellationToken.None);
    public ValueTask<IShadowRoot?> GetShadowRootAsync(CancellationToken cancellationToken)
        => GetShadowRootCoreAsync(cancellationToken);
    public ValueTask<IShadowRoot?> GetShadowRootAsync() => GetShadowRootAsync(CancellationToken.None);

    private async ValueTask<IShadowRoot?> GetShadowRootCoreAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        OwnerPage.OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebElementShadowRootLookupStarting(handle, OwnerPage.TabId);

        if (BridgeElementId is null || OwnerPage.BridgeCommands is not { } bridge)
            return null;

        var hasShadowRoot = await bridge.CheckShadowRootAsync(BridgeElementId, cancellationToken).ConfigureAwait(false);
        return hasShadowRoot ? new ShadowRoot(this, OwnerPage, Frame) : null;
    }

    private async ValueTask<Point> ResolveInteractionPointAsync(CancellationToken cancellationToken)
    {
        if (OwnerPage.BridgeCommands is { } bridge && BridgeElementId is { } elementId)
        {
            var viewportPoint = await TryResolveInteractionViewportPointFromPageContextAsync(cancellationToken).ConfigureAwait(false)
                ?? await bridge.ResolveElementScreenPointAsync(elementId, scrollIntoView: true, cancellationToken).ConfigureAwait(false);
            return await OwnerPage.ResolveViewportToScreenAsync(viewportPoint.X, viewportPoint.Y, cancellationToken).ConfigureAwait(false);
        }

        var bounds = await GetBoundingBoxAsync(cancellationToken).ConfigureAwait(false);
        if (bounds is not Rectangle rectangle)
            return Point.Empty;

        var viewportX = rectangle.Left + (rectangle.Width / 2f);
        var viewportY = rectangle.Top + (rectangle.Height / 2f);
        return await OwnerPage.ResolveViewportToScreenAsync(viewportX, viewportY, cancellationToken).ConfigureAwait(false);
    }

    internal void AttachContentFrame(IFrame frame)
        => contentFrame = frame;

    private async ValueTask<IElement?> GetParentElementCoreAsync(CancellationToken cancellationToken)
    {
        var state = await ResolveTraversalStateAsync(cancellationToken).ConfigureAwait(false);
        if (state is null)
            return null;

        var parentPath = GetParentPath(state.Path);
        if (string.IsNullOrWhiteSpace(parentPath))
            return null;

        var parentState = HtmlFallbackDomQuery.FindByPath(OwnerPage.CurrentContent, parentPath);
        return parentState is null ? null : CreateTraversalElement(parentState);
    }

    private async ValueTask<IEnumerable<IElement>> GetChildElementsCoreAsync(CancellationToken cancellationToken)
    {
        var state = await ResolveTraversalStateAsync(cancellationToken).ConfigureAwait(false);
        if (state is null)
            return [];

        return CreateTraversalElements(HtmlFallbackDomQuery.FindChildren(OwnerPage.CurrentContent, state.Path));
    }

    private async ValueTask<IEnumerable<IElement>> GetSiblingElementsCoreAsync(CancellationToken cancellationToken)
    {
        var state = await ResolveTraversalStateAsync(cancellationToken).ConfigureAwait(false);
        if (state is null)
            return [];

        return CreateTraversalElements(HtmlFallbackDomQuery.FindSiblings(OwnerPage.CurrentContent, state.Path));
    }

    private async ValueTask<string?> GetElementPathCoreAsync(CancellationToken cancellationToken)
    {
        var state = await ResolveTraversalStateAsync(cancellationToken).ConfigureAwait(false);
        return state?.Path;
    }

    private async ValueTask<HtmlFallbackElementState?> ResolveTraversalStateAsync(CancellationToken cancellationToken)
    {
        if (fallbackState is not null)
            return fallbackState;

        if (OwnerPage.BridgeCommands is null)
            return HtmlFallbackElementState.Create(OwnerPage.CurrentContent);

        var elementId = await GetIdAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(elementId))
            return HtmlFallbackDomQuery.FindFirst(OwnerPage.CurrentContent, new ElementSelector(ElementSelectorStrategy.Id, elementId));

        return null;
    }

    private Element CreateTraversalElement(HtmlFallbackElementState state)
        => new(OwnerPage, Frame, fallbackState: state);

    private IElement[] CreateTraversalElements(IEnumerable<HtmlFallbackElementState> states)
        => states.Select(CreateTraversalElement).Cast<IElement>().ToArray();

    private static string? GetParentPath(string path)
    {
        var lastSeparatorIndex = path.LastIndexOf('/');
        return lastSeparatorIndex <= 0 ? null : path[..lastSeparatorIndex];
    }

    private async ValueTask<string?> GetBridgePropertyOrFallbackAsync(
        string propertyName,
        Func<HtmlFallbackElementState, string?> fallbackAccessor,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (OwnerPage.BridgeCommands is { } bridge && BridgeElementId is { } elementId)
            return await bridge.GetElementPropertyAsync(elementId, propertyName, cancellationToken).ConfigureAwait(false);

        var state = GetFallbackState();
        return state is null ? null : fallbackAccessor(state);
    }

    private async ValueTask<bool> GetBridgeBooleanPropertyOrFallbackAsync(
        string propertyName,
        Func<HtmlFallbackElementState, bool> fallbackAccessor,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (OwnerPage.BridgeCommands is { } bridge && BridgeElementId is { } elementId)
        {
            var value = await bridge.GetElementPropertyAsync(elementId, propertyName, cancellationToken).ConfigureAwait(false);
            return bool.TryParse(value, out var booleanValue) && booleanValue;
        }

        var state = GetFallbackState();
        return state is not null && fallbackAccessor(state);
    }

    private async ValueTask<Protocol.BridgeElementDescriptionPayload?> TryDescribeBridgeElementAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (OwnerPage.BridgeCommands is not { } bridge || BridgeElementId is not { } elementId)
            return null;

        return await bridge.DescribeElementAsync(elementId, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ExecuteElementScriptAsync(string script, CancellationToken cancellationToken)
        => _ = await EvaluateElementScriptCoreAsync(script, preferPageContextOnNull: false, forcePageContextExecution: false, cancellationToken).ConfigureAwait(false);

    private async ValueTask<JsonElement?> EvaluateElementScriptCoreAsync(string script, bool preferPageContextOnNull, bool forcePageContextExecution, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (OwnerPage.BridgeCommands is { } bridge && BridgeElementId is { } elementId)
        {
            return await bridge.ExecuteScriptAsync(
                script,
                shadowHostElementId: null,
                frameHostElementId: null,
                elementId: elementId,
                preferPageContextOnNull: preferPageContextOnNull,
                forcePageContextExecution: forcePageContextExecution,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (this.Frame is global::Atom.Net.Browsing.WebDriver.Frame concreteFrame)
            return await concreteFrame.EvaluateScriptCoreAsync(script, preferPageContextOnNull, cancellationToken).ConfigureAwait(false);

        return await this.Frame.EvaluateAsync(script, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<PointF?> TryResolveInteractionViewportPointFromPageContextAsync(CancellationToken cancellationToken)
    {
        var payload = await EvaluateElementScriptCoreAsync(
            """
const waitForLayoutMeasurement = (frameCount = 2) => new Promise((resolve) => {
    let remaining = frameCount;

    const advance = () => {
        remaining--;
        if (remaining <= 0) {
            resolve();
            return;
        }

        if (typeof globalThis.requestAnimationFrame === 'function') {
            globalThis.requestAnimationFrame(() => advance());
            return;
        }

        globalThis.setTimeout(() => advance(), 16);
    };

    advance();
});

element.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
await waitForLayoutMeasurement();

let viewportX = 0;
let viewportY = 0;

for (let attempt = 0; attempt < 24; attempt++) {
    const rect = element.getBoundingClientRect();
    viewportX = rect.width > 0 ? rect.left + (rect.width / 2) : rect.left;
    viewportY = rect.height > 0 ? rect.top + (rect.height / 2) : rect.top;

    const hitTarget = document.elementFromPoint(viewportX, viewportY);
    if (hitTarget === element || (hitTarget instanceof Element && element.contains(hitTarget))) {
        break;
    }

    await waitForLayoutMeasurement(1);
}

return JSON.stringify({ viewportX, viewportY });
""",
            preferPageContextOnNull: true,
            forcePageContextExecution: true,
            cancellationToken).ConfigureAwait(false);

        return TryParseViewportPoint(payload, out var viewportPoint)
            ? viewportPoint
            : null;
    }

    private async ValueTask<PointerCalibrationState?> ResetPointerCalibrationStateAsync(CancellationToken cancellationToken)
    {
        var payload = await EvaluateElementScriptCoreAsync(
            """
const stateKey = '__atomTrustedPointerCalibration';
const state = globalThis[stateKey] ?? (globalThis[stateKey] = {
    installed: false,
    lastClientX: null,
    lastClientY: null,
    lastTrusted: false,
});

if (!state.installed) {
    const update = (event) => {
        if (!event?.isTrusted) {
            return;
        }

        state.lastClientX = typeof event.clientX === 'number' ? event.clientX : null;
        state.lastClientY = typeof event.clientY === 'number' ? event.clientY : null;
        state.lastTrusted = true;
    };

    document.addEventListener('pointermove', update, true);
    document.addEventListener('mousemove', update, true);
    state.installed = true;
}

state.lastClientX = null;
state.lastClientY = null;
state.lastTrusted = false;

const rect = element.getBoundingClientRect();
return JSON.stringify({
    targetCenterX: rect.width > 0 ? rect.left + (rect.width / 2) : rect.left,
    targetCenterY: rect.height > 0 ? rect.top + (rect.height / 2) : rect.top,
    lastClientX: state.lastClientX,
    lastClientY: state.lastClientY,
    lastTrusted: state.lastTrusted,
    hovered: element.matches(':hover'),
});
""",
            preferPageContextOnNull: true,
            forcePageContextExecution: true,
            cancellationToken).ConfigureAwait(false);

        return TryParsePointerCalibrationState(payload, out var calibrationState)
            ? calibrationState
            : null;
    }

    private async ValueTask<PointerCalibrationState?> ReadPointerCalibrationStateAsync(CancellationToken cancellationToken)
    {
        var payload = await EvaluateElementScriptCoreAsync(
            """
const stateKey = '__atomTrustedPointerCalibration';
const state = globalThis[stateKey] ?? {
    lastClientX: null,
    lastClientY: null,
    lastTrusted: false,
};
const rect = element.getBoundingClientRect();
return JSON.stringify({
    targetCenterX: rect.width > 0 ? rect.left + (rect.width / 2) : rect.left,
    targetCenterY: rect.height > 0 ? rect.top + (rect.height / 2) : rect.top,
    lastClientX: state.lastClientX,
    lastClientY: state.lastClientY,
    lastTrusted: state.lastTrusted,
    hovered: element.matches(':hover'),
});
""",
            preferPageContextOnNull: true,
            forcePageContextExecution: true,
            cancellationToken).ConfigureAwait(false);

        return TryParsePointerCalibrationState(payload, out var calibrationState)
            ? calibrationState
            : null;
    }

    private static bool TryParseViewportPoint(JsonElement? payload, out PointF viewportPoint)
    {
        viewportPoint = default;
        if (payload is not { } jsonPayload)
            return false;

        JsonElement pointElement;
        JsonDocument? pointDocument = null;

        try
        {
            if (jsonPayload.ValueKind == JsonValueKind.String)
            {
                var json = jsonPayload.GetString();
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                pointDocument = JsonDocument.Parse(json);
                pointElement = pointDocument.RootElement;
            }
            else
            {
                pointElement = jsonPayload;
            }

            if (pointElement.ValueKind != JsonValueKind.Object
                || !pointElement.TryGetProperty("viewportX", out var viewportX)
                || !pointElement.TryGetProperty("viewportY", out var viewportY)
                || !viewportX.TryGetSingle(out var viewportXValue)
                || !viewportY.TryGetSingle(out var viewportYValue))
            {
                return false;
            }

            viewportPoint = new PointF(viewportXValue, viewportYValue);
            return true;
        }
        finally
        {
            pointDocument?.Dispose();
        }
    }

    private static bool TryParsePointerCalibrationState(JsonElement? payload, out PointerCalibrationState calibrationState)
    {
        calibrationState = default;
        if (payload is not { } jsonPayload)
            return false;

        JsonElement stateElement;
        JsonDocument? stateDocument = null;

        try
        {
            if (jsonPayload.ValueKind == JsonValueKind.String)
            {
                var json = jsonPayload.GetString();
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                stateDocument = JsonDocument.Parse(json);
                stateElement = stateDocument.RootElement;
            }
            else
            {
                stateElement = jsonPayload;
            }

            if (stateElement.ValueKind != JsonValueKind.Object)
                return false;

            calibrationState = new PointerCalibrationState(
                ReadNullableSingle(stateElement, "targetCenterX"),
                ReadNullableSingle(stateElement, "targetCenterY"),
                ReadNullableSingle(stateElement, "lastClientX"),
                ReadNullableSingle(stateElement, "lastClientY"),
                ReadBoolean(stateElement, "lastTrusted"),
                ReadBoolean(stateElement, "hovered"));

            return true;
        }
        finally
        {
            stateDocument?.Dispose();
        }
    }

    private static float? ReadNullableSingle(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Null)
            return null;

        return property.TryGetSingle(out var value) ? value : null;
    }

    private static bool ReadBoolean(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            && property.GetBoolean();

    private readonly record struct PointerCalibrationState(
        float? TargetCenterX,
        float? TargetCenterY,
        float? LastClientX,
        float? LastClientY,
        bool LastTrusted,
        bool IsHovered);

    private static string SerializeJavaScriptString(string value)
        => string.Concat('"', JavaScriptEncoder.Default.Encode(value), '"');

    private static string CreateAddEventListenerScript(string eventName, string listenerId, string callbackName)
        => $$"""
(() => {
    const eventName = {{SerializeJavaScriptString(eventName)}};
    const listenerId = {{SerializeJavaScriptString(listenerId)}};
    const callbackName = {{SerializeJavaScriptString(callbackName)}};
    const registryKey = {{SerializeJavaScriptString(ElementCallbackRegistryKey)}};
    const dispatchEventName = {{SerializeJavaScriptString(CallbackBridgeEventName)}};
    const target = element;
    if (!target) {
        return false;
    }

    const registry = target[registryKey] ?? (target[registryKey] = Object.create(null));
    if (registry[listenerId]) {
        target.removeEventListener(eventName, registry[listenerId]);
    }

    const publish = (payload) => {
        const root = document.documentElement ?? document.head ?? document.body;
        if (!root) {
            return;
        }

        const node = document.createElement('script');
        node.type = 'application/json';
        node.dataset.atomCallbackPayload = '1';
        node.textContent = JSON.stringify(payload);
        root.appendChild(node);
        try { document.dispatchEvent(new Event(dispatchEventName)); } catch { }
        try { globalThis.dispatchEvent(new Event(dispatchEventName)); } catch { }
    };

    const handler = (event) => {
        const currentTarget = event?.currentTarget;
        publish({
            name: callbackName,
            code: callbackName + '(<event>)',
            args: [JSON.stringify({
                type: event?.type ?? null,
                isTrusted: !!event?.isTrusted,
                targetId: event?.target && typeof event.target.id === 'string' ? event.target.id : null,
                currentTargetId: currentTarget && typeof currentTarget.id === 'string' ? currentTarget.id : null,
                value: currentTarget && 'value' in currentTarget ? currentTarget.value ?? null : null,
                key: event && 'key' in event ? event.key ?? null : null,
                code: event && 'code' in event ? event.code ?? null : null,
                button: event && 'button' in event && typeof event.button === 'number' ? event.button : null,
                clientX: event && 'clientX' in event && typeof event.clientX === 'number' ? event.clientX : null,
                clientY: event && 'clientY' in event && typeof event.clientY === 'number' ? event.clientY : null,
            })],
        });
    };

    registry[listenerId] = handler;
    target.addEventListener(eventName, handler);
    return true;
})();
""";

    private static string CreateRemoveEventListenerScript(string eventName, string listenerId)
        => $$"""
(() => {
    const eventName = {{SerializeJavaScriptString(eventName)}};
    const listenerId = {{SerializeJavaScriptString(listenerId)}};
    const registry = element?.[{{SerializeJavaScriptString(ElementCallbackRegistryKey)}}];
    if (!registry || !registry[listenerId]) {
        return false;
    }

    element.removeEventListener(eventName, registry[listenerId]);
    delete registry[listenerId];
    return true;
})();
""";

    private async ValueTask InvokeElementEventHandlerAsync(Delegate handler, object?[] args)
    {
        var parameters = handler.Method.GetParameters();
        var result = parameters.Length switch
        {
            0 => handler.DynamicInvoke(),
            1 => handler.DynamicInvoke(ResolveSingleParameterElementEventArgument(parameters[0].ParameterType, args)),
            2 when parameters[0].ParameterType.IsInstanceOfType(this) => handler.DynamicInvoke(this, ConvertElementEventPayload(parameters[1].ParameterType, args)),
            _ => throw new NotSupportedException($"Unsupported element event handler signature for '{handler.Method.Name}'."),
        };

        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            return;
        }

        if (result is ValueTask valueTask)
            await valueTask.ConfigureAwait(false);
    }

    private object? ResolveSingleParameterElementEventArgument(Type parameterType, object?[] args)
    {
        if (!parameterType.Equals(typeof(object)) && parameterType.IsInstanceOfType(this))
            return this;

        return ConvertElementEventPayload(parameterType, args);
    }

    private static object? ConvertElementEventPayload(Type parameterType, object?[] args)
    {
        var payload = args.Length > 0 ? args[0] : null;

        if (parameterType == typeof(object) || parameterType == typeof(string))
            return payload?.ToString();

        var parsedPayload = TryParseElementEventPayloadJson(payload);

        if (parameterType == typeof(JsonElement))
        {
            return parsedPayload ?? default;
        }

        if (parameterType == typeof(JsonElement?))
            return parsedPayload;

        if (parameterType.Equals(typeof(JsonNode)))
            return TryParseElementEventPayloadNode(payload);

        if (parameterType.Equals(typeof(JsonObject)))
            return TryParseElementEventPayloadNode(payload) as JsonObject ?? [];

        if (parameterType.Equals(typeof(Dictionary<string, JsonElement>))
            || parameterType.Equals(typeof(IDictionary<string, JsonElement>))
            || parameterType.Equals(typeof(IReadOnlyDictionary<string, JsonElement>)))
        {
            return TryCreateElementEventJsonElementDictionary(parsedPayload) ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }

        if (parameterType.Equals(typeof(Dictionary<string, object>))
            || parameterType.Equals(typeof(IDictionary<string, object>))
            || parameterType.Equals(typeof(IReadOnlyDictionary<string, object>)))
        {
            return TryCreateElementEventObjectDictionary(parsedPayload) ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        if (parameterType.IsAssignableFrom(typeof(ElementEventArgs)))
            return ElementEventArgs.FromPayload(parsedPayload ?? default);

        throw new NotSupportedException($"Unsupported element event payload parameter type '{parameterType.Name}'.");
    }

    private static JsonElement? TryParseElementEventPayloadJson(object? payload)
    {
        var payloadText = payload?.ToString();
        if (string.IsNullOrWhiteSpace(payloadText))
            return null;

        using var document = JsonDocument.Parse(payloadText);
        return document.RootElement.Clone();
    }

    private static JsonNode? TryParseElementEventPayloadNode(object? payload)
    {
        var payloadText = payload?.ToString();
        return string.IsNullOrWhiteSpace(payloadText) ? null : JsonNode.Parse(payloadText);
    }

    private static Dictionary<string, JsonElement>? TryCreateElementEventJsonElementDictionary(JsonElement? payload)
    {
        if (payload is not JsonElement jsonPayload || jsonPayload.ValueKind != JsonValueKind.Object)
            return null;

        Dictionary<string, JsonElement> result = new(StringComparer.Ordinal);
        foreach (var property in jsonPayload.EnumerateObject())
            result[property.Name] = property.Value.Clone();

        return result;
    }

    private static Dictionary<string, object?>? TryCreateElementEventObjectDictionary(JsonElement? payload)
    {
        if (payload is not JsonElement jsonPayload || jsonPayload.ValueKind != JsonValueKind.Object)
            return null;

        Dictionary<string, object?> result = new(StringComparer.Ordinal);
        foreach (var property in jsonPayload.EnumerateObject())
            result[property.Name] = ConvertJsonElementToObjectValue(property.Value);

        return result;
    }

    private static object? ConvertJsonElementToObjectValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Object => TryCreateElementEventObjectDictionary(value),
            JsonValueKind.Array => value.EnumerateArray().Select(ConvertJsonElementToObjectValue).ToArray(),
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var int64Value) => int64Value,
            JsonValueKind.Number when value.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True or JsonValueKind.False => value.GetBoolean(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.GetRawText(),
        };

    private sealed record ElementEventListenerRegistration(
        string ListenerId,
        string CallbackName,
        AsyncEventHandler<IWebPage, CallbackEventArgs> CallbackBridge);

    private static bool TryMapCharacterToKey(char character, out ConsoleKey key, out ConsoleModifiers modifiers)
    {
        if (TryMapAlphabeticCharacter(character, out key, out modifiers))
            return true;

        if (TryMapDigitCharacter(character, out key, out modifiers))
            return true;

        return TryMapSymbolCharacter(character, out key, out modifiers);
    }

    private static bool TryMapAlphabeticCharacter(char character, out ConsoleKey key, out ConsoleModifiers modifiers)
    {
        modifiers = default;

        if (character is >= 'a' and <= 'z')
        {
            key = Enum.Parse<ConsoleKey>(char.ToUpperInvariant(character).ToString(), ignoreCase: true);
            return true;
        }

        if (character is >= 'A' and <= 'Z')
        {
            key = Enum.Parse<ConsoleKey>(character.ToString(), ignoreCase: true);
            modifiers = ConsoleModifiers.Shift;
            return true;
        }

        key = default;
        return false;
    }

    private static bool TryMapDigitCharacter(char character, out ConsoleKey key, out ConsoleModifiers modifiers)
    {
        modifiers = default;

        if (character is < '0' or > '9')
        {
            key = default;
            return false;
        }

        key = character switch
        {
            '0' => ConsoleKey.D0,
            '1' => ConsoleKey.D1,
            '2' => ConsoleKey.D2,
            '3' => ConsoleKey.D3,
            '4' => ConsoleKey.D4,
            '5' => ConsoleKey.D5,
            '6' => ConsoleKey.D6,
            '7' => ConsoleKey.D7,
            '8' => ConsoleKey.D8,
            _ => ConsoleKey.D9,
        };

        return true;
    }

    private static bool TryMapSymbolCharacter(char character, out ConsoleKey key, out ConsoleModifiers modifiers)
    {
        return character switch
        {
            ' ' => SetMappedKey(ConsoleKey.Spacebar, out key, out modifiers),
            '\n' or '\r' => SetMappedKey(ConsoleKey.Enter, out key, out modifiers),
            '.' => SetMappedKey(ConsoleKey.OemPeriod, out key, out modifiers),
            ',' => SetMappedKey(ConsoleKey.OemComma, out key, out modifiers),
            '-' => SetMappedKey(ConsoleKey.OemMinus, out key, out modifiers),
            '_' => SetMappedKey(ConsoleKey.OemMinus, ConsoleModifiers.Shift, out key, out modifiers),
            '=' => SetMappedKey(ConsoleKey.OemPlus, out key, out modifiers),
            '+' => SetMappedKey(ConsoleKey.OemPlus, ConsoleModifiers.Shift, out key, out modifiers),
            ';' => SetMappedKey(ConsoleKey.Oem1, out key, out modifiers),
            ':' => SetMappedKey(ConsoleKey.Oem1, ConsoleModifiers.Shift, out key, out modifiers),
            '/' => SetMappedKey(ConsoleKey.Oem2, out key, out modifiers),
            '?' => SetMappedKey(ConsoleKey.Oem2, ConsoleModifiers.Shift, out key, out modifiers),
            '`' => SetMappedKey(ConsoleKey.Oem3, out key, out modifiers),
            '~' => SetMappedKey(ConsoleKey.Oem3, ConsoleModifiers.Shift, out key, out modifiers),
            '[' => SetMappedKey(ConsoleKey.Oem4, out key, out modifiers),
            '{' => SetMappedKey(ConsoleKey.Oem4, ConsoleModifiers.Shift, out key, out modifiers),
            '\\' => SetMappedKey(ConsoleKey.Oem5, out key, out modifiers),
            '|' => SetMappedKey(ConsoleKey.Oem5, ConsoleModifiers.Shift, out key, out modifiers),
            ']' => SetMappedKey(ConsoleKey.Oem6, out key, out modifiers),
            '}' => SetMappedKey(ConsoleKey.Oem6, ConsoleModifiers.Shift, out key, out modifiers),
            '\'' => SetMappedKey(ConsoleKey.Oem7, out key, out modifiers),
            '"' => SetMappedKey(ConsoleKey.Oem7, ConsoleModifiers.Shift, out key, out modifiers),
            '!' => SetMappedKey(ConsoleKey.D1, ConsoleModifiers.Shift, out key, out modifiers),
            '@' => SetMappedKey(ConsoleKey.D2, ConsoleModifiers.Shift, out key, out modifiers),
            '#' => SetMappedKey(ConsoleKey.D3, ConsoleModifiers.Shift, out key, out modifiers),
            '$' => SetMappedKey(ConsoleKey.D4, ConsoleModifiers.Shift, out key, out modifiers),
            '%' => SetMappedKey(ConsoleKey.D5, ConsoleModifiers.Shift, out key, out modifiers),
            '^' => SetMappedKey(ConsoleKey.D6, ConsoleModifiers.Shift, out key, out modifiers),
            '&' => SetMappedKey(ConsoleKey.D7, ConsoleModifiers.Shift, out key, out modifiers),
            '*' => SetMappedKey(ConsoleKey.D8, ConsoleModifiers.Shift, out key, out modifiers),
            '(' => SetMappedKey(ConsoleKey.D9, ConsoleModifiers.Shift, out key, out modifiers),
            ')' => SetMappedKey(ConsoleKey.D0, ConsoleModifiers.Shift, out key, out modifiers),
            _ => SetMappedKey(default, default, out key, out modifiers, success: false),
        };
    }

    private static bool SetMappedKey(ConsoleKey key, out ConsoleKey mappedKey, out ConsoleModifiers modifiers)
        => SetMappedKey(key, default, out mappedKey, out modifiers, success: true);

    private static bool SetMappedKey(ConsoleKey key, ConsoleModifiers modifiersValue, out ConsoleKey mappedKey, out ConsoleModifiers modifiers, bool success = true)
    {
        mappedKey = key;
        modifiers = modifiersValue;
        return success;
    }

    private HtmlFallbackElementState? GetFallbackState()
    {
        ThrowIfDisposed();
        if (fallbackState is not null)
            return fallbackState;

        var state = HtmlFallbackElementState.Create(OwnerPage.CurrentContent);
        if (state is not null)
            OwnerPage.OwnerWindow.OwnerBrowser.LaunchSettings.Logger?.LogWebElementFallbackActivated(handle, OwnerPage.TabId, state.TagName);

        return state;
    }
}