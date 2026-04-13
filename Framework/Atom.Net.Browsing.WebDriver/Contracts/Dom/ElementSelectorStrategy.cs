namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Определяет способ поиска DOM-элемента на странице.
/// </summary>
public enum ElementSelectorStrategy
{
    /// <summary>
    /// Поиск по CSS-селектору.
    /// </summary>
    Css,

    /// <summary>
    /// Поиск по XPath-выражению.
    /// </summary>
    XPath,

    /// <summary>
    /// Поиск по идентификатору элемента.
    /// </summary>
    Id,

    /// <summary>
    /// Поиск по текстовому содержимому.
    /// </summary>
    Text,

    /// <summary>
    /// Поиск по атрибуту имени.
    /// </summary>
    Name,

    /// <summary>
    /// Поиск по имени тега.
    /// </summary>
    TagName,
}