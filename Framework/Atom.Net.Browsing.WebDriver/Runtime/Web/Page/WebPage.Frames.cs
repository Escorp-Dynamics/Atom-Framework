namespace Atom.Net.Browsing.WebDriver;

public sealed partial class WebPage
{
    public async ValueTask<IFrame?> GetFrameAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (string.Equals(name, nameof(MainFrame), StringComparison.OrdinalIgnoreCase))
            return MainFrame;

        await EnsureFramesDiscoveredAsync(cancellationToken).ConfigureAwait(false);

        foreach (var frame in Frames)
        {
            if (await frame.IsDetachedAsync(cancellationToken).ConfigureAwait(false))
                continue;

            var frameName = await frame.GetNameAsync(cancellationToken).ConfigureAwait(false);
            if (string.Equals(frameName, name, StringComparison.Ordinal))
                return frame;
        }

        return null;
    }

    public ValueTask<IFrame?> GetFrameAsync(string name)
        => GetFrameAsync(name, CancellationToken.None);

    public async ValueTask<IFrame?> GetFrameAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        await EnsureFramesDiscoveredAsync(cancellationToken).ConfigureAwait(false);

        foreach (var frame in Frames)
        {
            if (await frame.IsDetachedAsync(cancellationToken).ConfigureAwait(false))
                continue;

            var frameUrl = await frame.GetUrlAsync(cancellationToken).ConfigureAwait(false);
            if (frameUrl == url)
                return frame;
        }

        return null;
    }

    public ValueTask<IFrame?> GetFrameAsync(Uri url)
        => GetFrameAsync(url, CancellationToken.None);

    public async ValueTask<IFrame?> GetFrameAsync(IElement element, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(element);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (!ReferenceEquals(element.Page, this))
            return null;

        return element.Frame;
    }

    public ValueTask<IFrame?> GetFrameAsync(IElement element)
        => GetFrameAsync(element, CancellationToken.None);
}