namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет корень Shadow DOM как отдельный ограниченный DOM-контекст.
/// </summary>
public interface IShadowRoot : IDomContext, IAsyncDisposable
{
    /// <summary>
    /// Получает элемент-хост, которому принадлежит shadow root.
    /// </summary>
    IElement Host { get; }

    /// <summary>
    /// Получает страницу, которой принадлежит shadow root.
    /// </summary>
    IWebPage Page { get; }

    /// <summary>
    /// Получает фрейм, в контексте которого существует shadow root.
    /// </summary>
    IFrame Frame { get; }
}