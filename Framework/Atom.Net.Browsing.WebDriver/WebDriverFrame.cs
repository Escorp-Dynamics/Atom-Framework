using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Реализация <see cref="IFrame"/> для WebDriver-моста.
/// Главный фрейм страницы использует тот же канал связи, что и вкладка.
/// </summary>
internal sealed class WebDriverFrame(TabChannel channel) : IFrame
{
    /// <inheritdoc />
    public string? Name => null;

    /// <inheritdoc />
    public Uri? Source => null;

    /// <inheritdoc />
    public IEnumerable<IFrame> Frames => [];

    /// <inheritdoc />
    public async ValueTask<Uri?> GetUrlAsync(CancellationToken cancellationToken)
    {
        var response = await channel.SendCommandAsync(
            BridgeCommand.GetUrl,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return response.Payload is JsonElement el && el.GetString() is string url
            ? new Uri(url)
            : null;
    }

    /// <inheritdoc cref="IDomContext.GetUrlAsync(CancellationToken)"/>
    public ValueTask<Uri?> GetUrlAsync() => GetUrlAsync(CancellationToken.None);

    /// <inheritdoc />
    public async ValueTask<string?> GetTitleAsync(CancellationToken cancellationToken)
    {
        var response = await channel.SendCommandAsync(
            BridgeCommand.GetTitle,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return response.Payload is JsonElement el ? el.GetString() : null;
    }

    /// <inheritdoc cref="IDomContext.GetTitleAsync(CancellationToken)"/>
    public ValueTask<string?> GetTitleAsync() => GetTitleAsync(CancellationToken.None);

    /// <inheritdoc />
    public async ValueTask<string?> GetContentAsync(CancellationToken cancellationToken)
    {
        var response = await channel.SendCommandAsync(
            BridgeCommand.GetContent,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return response.Payload is JsonElement el ? el.GetString() : null;
    }

    /// <inheritdoc cref="IDomContext.GetContentAsync(CancellationToken)"/>
    public ValueTask<string?> GetContentAsync() => GetContentAsync(CancellationToken.None);

    /// <inheritdoc />
    public async ValueTask<JsonElement?> ExecuteAsync(string script, CancellationToken cancellationToken)
    {
        var response = await channel.SendCommandAsync(
            BridgeCommand.ExecuteScript,
            new JsonObject { ["script"] = script },
            cancellationToken).ConfigureAwait(false);

        if (response.Status == BridgeStatus.Error)
            throw new BridgeException($"ExecuteScript failed: {response.Error}");

        return response.Payload is JsonElement el ? el : null;
    }

    /// <inheritdoc cref="IDomContext.ExecuteAsync(string, CancellationToken)"/>
    public ValueTask<JsonElement?> ExecuteAsync(string script) => ExecuteAsync(script, CancellationToken.None);

    /// <inheritdoc />
    public async ValueTask<IElement?> FindElementAsync(ElementSelector selector, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var response = await channel.SendCommandAsync(
            BridgeCommand.FindElement,
            new JsonObject { ["strategy"] = selector.Strategy.ToString(), ["value"] = selector.Value },
            cancellationToken).ConfigureAwait(false);

        return response.Status == BridgeStatus.Ok && response.Payload is JsonElement el && el.GetString() is { } id
            ? new WebDriverElement(id, channel)
            : null;
    }

    /// <inheritdoc cref="IDomContext.FindElementAsync(ElementSelector, CancellationToken)"/>
    public ValueTask<IElement?> FindElementAsync(ElementSelector selector) => FindElementAsync(selector, CancellationToken.None);

    /// <inheritdoc />
    public async ValueTask<IElement[]> FindElementsAsync(ElementSelector selector, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var response = await channel.SendCommandAsync(
            BridgeCommand.FindElements,
            new JsonObject { ["strategy"] = selector.Strategy.ToString(), ["value"] = selector.Value },
            cancellationToken).ConfigureAwait(false);

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
        ArgumentNullException.ThrowIfNull(selector);

        var response = await channel.SendCommandAsync(
            BridgeCommand.WaitForElement,
            new JsonObject
            {
                ["strategy"] = selector.Strategy.ToString(),
                ["value"] = selector.Value,
                ["timeoutMs"] = (timeout ?? TimeSpan.FromSeconds(10)).TotalMilliseconds,
            },
            cancellationToken).ConfigureAwait(false);

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
}
