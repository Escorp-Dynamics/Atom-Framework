namespace Atom.Web.Browsing.Drivers;

/// <summary>
/// Режим работы драйвера веб-браузера.
/// </summary>
public enum WebDriverMode
{
    /// <summary>
    /// С графической оболочкой.
    /// </summary>
    Default,
    /// <summary>
    /// Без графической оболочки.
    /// </summary>
    Headless,
}