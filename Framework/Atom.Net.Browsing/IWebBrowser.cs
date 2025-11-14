namespace Atom.Net.Browsing;

/// <summary>
/// Представляет браузер.
/// </summary>
public interface IWebBrowser : IAsyncDisposable
{
    /// <summary>
    /// Открытые окна браузера.
    /// </summary>
    IEnumerable<IWebWindow> Windows { get; }

    /// <summary>
    /// Текущее активное окно.
    /// </summary>
    IWebWindow CurrentWindow { get; }

    /// <summary>
    /// Открытые вкладки браузера.
    /// </summary>
    IEnumerable<IWebPage> Pages => Windows.SelectMany(x => x.Pages);

    /// <summary>
    /// Текущая активная вкладка браузера.
    /// </summary>
    IWebPage CurrentPage => CurrentWindow.CurrentPage;
}