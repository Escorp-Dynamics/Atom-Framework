namespace Atom.Net.Browsing;

/// <summary>
/// Селектор для поиска элементов на странице.
/// </summary>
public sealed class ElementSelector
{
    /// <summary>
    /// Стратегия поиска элемента.
    /// </summary>
    public required ElementSelectorStrategy Strategy { get; init; }

    /// <summary>
    /// Значение селектора.
    /// </summary>
    public required string Value { get; init; }
}

/// <summary>
/// Стратегия поиска элемента на странице.
/// </summary>
public enum ElementSelectorStrategy
{
    /// <summary>
    /// CSS-селектор.
    /// </summary>
    Css,

    /// <summary>
    /// XPath-выражение.
    /// </summary>
    XPath,

    /// <summary>
    /// По идентификатору элемента.
    /// </summary>
    Id,

    /// <summary>
    /// По тексту содержимого.
    /// </summary>
    Text,

    /// <summary>
    /// По значению атрибута name.
    /// </summary>
    Name,

    /// <summary>
    /// По имени тега.
    /// </summary>
    TagName,
}
