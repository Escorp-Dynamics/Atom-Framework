namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет данные публичного lifecycle-события страницы, окна или браузера.
/// </summary>
public sealed class WebLifecycleEventArgs : EventArgs
{
    public required IWebWindow Window { get; init; }

    public required IWebPage Page { get; init; }

    public required IFrame Frame { get; init; }

    public Uri? Url { get; init; }

    public string? Title { get; init; }
}