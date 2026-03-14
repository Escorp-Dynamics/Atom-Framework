using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Реализация <see cref="IShadowRoot"/> для WebDriver-моста.
/// Все DOM-операции делегируются расширению с указанием хост-элемента,
/// обеспечивая скоупинг внутри теневого дерева.
/// </summary>
internal sealed class WebDriverShadowRoot(string hostElementId, TabChannel channel, bool isClosed = false) : IShadowRoot
{
    private bool isDisposed;

    /// <inheritdoc />
    public IEnumerable<IFrame> Frames => [];

    /// <inheritdoc />
    public ValueTask<Uri?> GetUrlAsync(CancellationToken cancellationToken) => default;

    /// <inheritdoc cref="IDomContext.GetUrlAsync(CancellationToken)"/>
    public ValueTask<Uri?> GetUrlAsync() => default;

    /// <inheritdoc />
    public ValueTask<string?> GetTitleAsync(CancellationToken cancellationToken) => default;

    /// <inheritdoc cref="IDomContext.GetTitleAsync(CancellationToken)"/>
    public ValueTask<string?> GetTitleAsync() => default;

    /// <inheritdoc />
    public async ValueTask<string?> GetContentAsync(CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync("return shadowRoot.innerHTML", cancellationToken).ConfigureAwait(false);
        return result?.GetString();
    }

    /// <inheritdoc cref="IDomContext.GetContentAsync(CancellationToken)"/>
    public ValueTask<string?> GetContentAsync() => GetContentAsync(CancellationToken.None);

    /// <inheritdoc />
    public async ValueTask<JsonElement?> ExecuteAsync(string script, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var response = await channel.SendCommandAsync(
            BridgeCommand.ExecuteScript,
            new JsonObject
            {
                ["script"] = script,
                ["shadowHostElementId"] = hostElementId,
            },
            cancellationToken).ConfigureAwait(false);

        if (response.Status == BridgeStatus.Error)
            throw new BridgeException($"ExecuteScript in shadow root failed: {response.Error}");

        return response.Payload is JsonElement el ? el : null;
    }

    /// <inheritdoc cref="IDomContext.ExecuteAsync(string, CancellationToken)"/>
    public ValueTask<JsonElement?> ExecuteAsync(string script) => ExecuteAsync(script, CancellationToken.None);

    /// <inheritdoc />
    public async ValueTask<IElement?> FindElementAsync(ElementSelector selector, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(selector);

        var findPayload = new JsonObject
        {
            ["strategy"] = selector.Strategy.ToString(),
            ["value"] = selector.Value,
            ["shadowHostElementId"] = hostElementId,
        };

        if (isClosed)
            findPayload["closedShadow"] = true;

        var response = await channel.SendCommandAsync(
            BridgeCommand.FindElement, findPayload, cancellationToken).ConfigureAwait(false);

        return response.Status == BridgeStatus.Ok && response.Payload is JsonElement el && el.GetString() is { } id
            ? new WebDriverElement(id, channel)
            : null;
    }

    /// <inheritdoc cref="IDomContext.FindElementAsync(ElementSelector, CancellationToken)"/>
    public ValueTask<IElement?> FindElementAsync(ElementSelector selector) => FindElementAsync(selector, CancellationToken.None);

    /// <inheritdoc />
    public async ValueTask<IElement[]> FindElementsAsync(ElementSelector selector, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(selector);

        var findPayload = new JsonObject
        {
            ["strategy"] = selector.Strategy.ToString(),
            ["value"] = selector.Value,
            ["shadowHostElementId"] = hostElementId,
        };

        if (isClosed)
            findPayload["closedShadow"] = true;

        var response = await channel.SendCommandAsync(
            BridgeCommand.FindElements, findPayload, cancellationToken).ConfigureAwait(false);

        if (response.Status != BridgeStatus.Ok || response.Payload is not JsonElement el)
            return [];

        var ids = el.Deserialize(BridgePageJsonContext.Default.StringArray) ?? [];
        return [.. ids.Select(id => (IElement)new WebDriverElement(id, channel))];
    }

    /// <inheritdoc cref="IDomContext.FindElementsAsync(ElementSelector, CancellationToken)"/>
    public ValueTask<IElement[]> FindElementsAsync(ElementSelector selector) => FindElementsAsync(selector, CancellationToken.None);

    /// <inheritdoc />
    public async ValueTask<IElement?> WaitForElementAsync(
        ElementSelector selector,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(selector);

        var waitPayload = new JsonObject
        {
            ["strategy"] = selector.Strategy.ToString(),
            ["value"] = selector.Value,
            ["timeoutMs"] = (timeout ?? TimeSpan.FromSeconds(10)).TotalMilliseconds,
            ["shadowHostElementId"] = hostElementId,
        };

        if (isClosed)
            waitPayload["closedShadow"] = true;

        var response = await channel.SendCommandAsync(
            BridgeCommand.WaitForElement, waitPayload, cancellationToken).ConfigureAwait(false);

        return response.Status == BridgeStatus.Ok && response.Payload is JsonElement el && el.GetString() is { } id
            ? new WebDriverElement(id, channel)
            : null;
    }

    /// <inheritdoc cref="IDomContext.WaitForElementAsync(ElementSelector, TimeSpan?, CancellationToken)"/>
    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, TimeSpan? timeout)
        => WaitForElementAsync(selector, timeout, CancellationToken.None);

    /// <inheritdoc cref="IDomContext.WaitForElementAsync(ElementSelector, TimeSpan?, CancellationToken)"/>
    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, CancellationToken cancellationToken)
        => WaitForElementAsync(selector, timeout: null, cancellationToken);

    /// <inheritdoc cref="IDomContext.WaitForElementAsync(ElementSelector, TimeSpan?, CancellationToken)"/>
    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector)
        => WaitForElementAsync(selector, timeout: null, CancellationToken.None);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        isDisposed = true;
        return default;
    }
}
