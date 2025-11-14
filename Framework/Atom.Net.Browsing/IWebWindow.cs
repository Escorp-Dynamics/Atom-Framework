namespace Atom.Net.Browsing;

/// <summary>
/// Представляет окно браузера.
/// </summary>
public interface IWebWindow : IAsyncDisposable
{
    /// <summary>
    /// Открытые вкладки окна.
    /// </summary>
    IEnumerable<IWebPage> Pages { get; }

    /// <summary>
    /// Текущая активная вкладка.
    /// </summary>
    IWebPage CurrentPage { get; }
}