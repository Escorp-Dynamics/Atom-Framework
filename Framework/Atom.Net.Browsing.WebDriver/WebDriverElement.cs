using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Реализация <see cref="IElement"/> для WebDriver-моста.
/// </summary>
internal sealed class WebDriverElement(string elementId, TabChannel channel) : IElement
{
    /// <inheritdoc />
    public string Id => elementId;

    /// <inheritdoc />
    public ValueTask ClickAsync(CancellationToken cancellationToken)
        => ActionAsync(channel, elementId, ElementActionType.Click, value: null, cancellationToken);

    /// <inheritdoc cref="IElement.ClickAsync(CancellationToken)"/>
    public ValueTask ClickAsync() => ClickAsync(CancellationToken.None);

    /// <inheritdoc />
    public ValueTask DoubleClickAsync(CancellationToken cancellationToken)
        => ActionAsync(channel, elementId, ElementActionType.DoubleClick, value: null, cancellationToken);

    /// <inheritdoc cref="IElement.DoubleClickAsync(CancellationToken)"/>
    public ValueTask DoubleClickAsync() => DoubleClickAsync(CancellationToken.None);

    /// <inheritdoc />
    public ValueTask TypeAsync(string text, CancellationToken cancellationToken)
        => ActionAsync(channel, elementId, ElementActionType.Type, text, cancellationToken);

    /// <inheritdoc cref="IElement.TypeAsync(string, CancellationToken)"/>
    public ValueTask TypeAsync(string text) => TypeAsync(text, CancellationToken.None);

    /// <inheritdoc />
    public ValueTask ClearAsync(CancellationToken cancellationToken)
        => ActionAsync(channel, elementId, ElementActionType.Clear, value: null, cancellationToken);

    /// <inheritdoc cref="IElement.ClearAsync(CancellationToken)"/>
    public ValueTask ClearAsync() => ClearAsync(CancellationToken.None);

    /// <inheritdoc />
    public ValueTask HoverAsync(CancellationToken cancellationToken)
        => ActionAsync(channel, elementId, ElementActionType.Hover, value: null, cancellationToken);

    /// <inheritdoc cref="IElement.HoverAsync(CancellationToken)"/>
    public ValueTask HoverAsync() => HoverAsync(CancellationToken.None);

    /// <inheritdoc />
    public ValueTask FocusAsync(CancellationToken cancellationToken)
        => ActionAsync(channel, elementId, ElementActionType.Focus, value: null, cancellationToken);

    /// <inheritdoc cref="IElement.FocusAsync(CancellationToken)"/>
    public ValueTask FocusAsync() => FocusAsync(CancellationToken.None);

    /// <inheritdoc />
    public ValueTask ScrollIntoViewAsync(CancellationToken cancellationToken)
        => ActionAsync(channel, elementId, ElementActionType.ScrollIntoView, value: null, cancellationToken);

    /// <inheritdoc cref="IElement.ScrollIntoViewAsync(CancellationToken)"/>
    public ValueTask ScrollIntoViewAsync() => ScrollIntoViewAsync(CancellationToken.None);

    /// <inheritdoc />
    public ValueTask SelectAsync(string value, CancellationToken cancellationToken)
        => ActionAsync(channel, elementId, ElementActionType.Select, value, cancellationToken);

    /// <inheritdoc cref="IElement.SelectAsync(string, CancellationToken)"/>
    public ValueTask SelectAsync(string value) => SelectAsync(value, CancellationToken.None);

    /// <inheritdoc />
    public ValueTask CheckAsync(CancellationToken cancellationToken)
        => ActionAsync(channel, elementId, ElementActionType.Check, value: null, cancellationToken);

    /// <inheritdoc cref="IElement.CheckAsync(CancellationToken)"/>
    public ValueTask CheckAsync() => CheckAsync(CancellationToken.None);

    /// <inheritdoc />
    public async ValueTask<string?> GetPropertyAsync(string propertyName, CancellationToken cancellationToken)
    {
        var response = await channel.SendCommandAsync(
            BridgeCommand.GetElementProperty,
            new JsonObject { ["elementId"] = elementId, ["propertyName"] = propertyName },
            cancellationToken).ConfigureAwait(false);

        return response.Payload is JsonElement el ? el.GetString() : null;
    }

    /// <inheritdoc cref="IElement.GetPropertyAsync(string, CancellationToken)"/>
    public ValueTask<string?> GetPropertyAsync(string propertyName)
        => GetPropertyAsync(propertyName, CancellationToken.None);

    /// <inheritdoc />
    public async ValueTask<IElement?> FindElementAsync(ElementSelector selector, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var response = await channel.SendCommandAsync(
            BridgeCommand.FindElement,
            new JsonObject
            {
                ["strategy"] = selector.Strategy.ToString(),
                ["value"] = selector.Value,
                ["parentElementId"] = elementId,
            },
            cancellationToken).ConfigureAwait(false);

        return response.Status == BridgeStatus.Ok && response.Payload is JsonElement el && el.GetString() is { } id
            ? new WebDriverElement(id, channel)
            : null;
    }

    /// <inheritdoc cref="IElement.FindElementAsync(ElementSelector, CancellationToken)"/>
    public ValueTask<IElement?> FindElementAsync(ElementSelector selector)
        => FindElementAsync(selector, CancellationToken.None);

    /// <inheritdoc />
    public async ValueTask<IElement[]> FindElementsAsync(ElementSelector selector, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var response = await channel.SendCommandAsync(
            BridgeCommand.FindElements,
            new JsonObject
            {
                ["strategy"] = selector.Strategy.ToString(),
                ["value"] = selector.Value,
                ["parentElementId"] = elementId,
            },
            cancellationToken).ConfigureAwait(false);

        if (response.Status != BridgeStatus.Ok || response.Payload is not JsonElement el)
            return [];

        var ids = el.Deserialize(BridgePageJsonContext.Default.StringArray) ?? [];
        return [.. ids.Select(id => (IElement)new WebDriverElement(id, channel))];
    }

    /// <inheritdoc cref="IElement.FindElementsAsync(ElementSelector, CancellationToken)"/>
    public ValueTask<IElement[]> FindElementsAsync(ElementSelector selector)
        => FindElementsAsync(selector, CancellationToken.None);

    /// <inheritdoc />
    public async ValueTask<IShadowRoot?> OpenShadowRootAsync(CancellationToken cancellationToken)
    {
        var response = await channel.SendCommandAsync(
            BridgeCommand.CheckShadowRoot,
            new JsonObject { ["elementId"] = elementId },
            cancellationToken).ConfigureAwait(false);

        if (response.Status != BridgeStatus.Ok || response.Payload is not JsonElement el || !el.GetBoolean())
            return null;

        return new WebDriverShadowRoot(elementId, channel);
    }

    /// <inheritdoc cref="IElement.OpenShadowRootAsync(CancellationToken)"/>
    public ValueTask<IShadowRoot?> OpenShadowRootAsync() => OpenShadowRootAsync(CancellationToken.None);

    private static async ValueTask ActionAsync(
        TabChannel channel, string elementId, ElementActionType action, string? value, CancellationToken cancellationToken)
    {
        await channel.SendCommandAsync(
            BridgeCommand.ElementAction,
            new JsonObject { ["elementId"] = elementId, ["action"] = action.ToString(), ["value"] = value },
            cancellationToken).ConfigureAwait(false);
    }
}
