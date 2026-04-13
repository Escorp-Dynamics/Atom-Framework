using System.Text.Json;

namespace Atom.Net.Browsing.WebDriver;

public sealed partial class WebPage
{
    internal ValueTask<JsonElement?> ExecuteInAllFramesAsync(string script)
        => ExecuteInAllFramesAsync(script, CancellationToken.None);

    internal async ValueTask<JsonElement?> ExecuteInAllFramesAsync(string script, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (BridgeCommands is not { } bridge)
            return null;

        return await bridge.ExecuteScriptInFramesAsync(script, isolatedWorld: false, includeMetadata: false, cancellationToken).ConfigureAwait(false);
    }

    internal ValueTask<JsonElement?> ExecuteInAllFramesIsolatedAsync(string script)
        => ExecuteInAllFramesIsolatedAsync(script, CancellationToken.None);

    internal async ValueTask<JsonElement?> ExecuteInAllFramesIsolatedAsync(string script, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (BridgeCommands is not { } bridge)
            return null;

        return await bridge.ExecuteScriptInFramesAsync(script, isolatedWorld: true, includeMetadata: false, cancellationToken).ConfigureAwait(false);
    }

    internal ValueTask<JsonElement?> ExecuteInAllFramesWithMetadataAsync(string script)
        => ExecuteInAllFramesWithMetadataAsync(script, isolatedWorld: false, CancellationToken.None);

    internal ValueTask<JsonElement?> ExecuteInAllFramesWithMetadataAsync(string script, bool isolatedWorld)
        => ExecuteInAllFramesWithMetadataAsync(script, isolatedWorld, CancellationToken.None);

    internal async ValueTask<JsonElement?> ExecuteInAllFramesWithMetadataAsync(
        string script,
        bool isolatedWorld,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (BridgeCommands is not { } bridge)
            return null;

        return await bridge.ExecuteScriptInFramesAsync(script, isolatedWorld, includeMetadata: true, cancellationToken).ConfigureAwait(false);
    }

    internal ValueTask<bool> WaitForSelectorInFramesAsync(string selector)
        => WaitForSelectorInFramesAsync(selector, timeout: null, CancellationToken.None);

    internal ValueTask<bool> WaitForSelectorInFramesAsync(string selector, TimeSpan? timeout)
        => WaitForSelectorInFramesAsync(selector, timeout, CancellationToken.None);

    internal ValueTask<bool> WaitForSelectorInFramesAsync(string selector, CancellationToken cancellationToken)
        => WaitForSelectorInFramesAsync(selector, timeout: null, cancellationToken);

    internal async ValueTask<bool> WaitForSelectorInFramesAsync(
        string selector,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        var escapedSelector = selector.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
        var script = $"document.querySelector('{escapedSelector}') !== null";

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var results = await ExecuteInAllFramesAsync(script, cancellationToken).ConfigureAwait(false);
            if (HasFrameSelectorMatch(results))
                return true;

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static bool HasFrameSelectorMatch(JsonElement? results)
    {
        if (results is not JsonElement array || array.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.True)
                return true;

            if (item.ValueKind == JsonValueKind.String
                && string.Equals(item.GetString(), bool.TrueString, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}