namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет селектор для поиска элемента в DOM.
/// </summary>
public class ElementSelector
{
    /// <summary>
    /// Инициализирует пустой селектор.
    /// </summary>
    private ElementSelector() { }

    /// <summary>
    /// Инициализирует селектор с заданной стратегией и значением.
    /// </summary>
    /// <param name="strategy">Стратегия поиска элемента.</param>
    /// <param name="value">Значение селектора.</param>
    public ElementSelector(ElementSelectorStrategy strategy, string value)
        => (Strategy, Value) = (strategy, value);

    /// <summary>
    /// Получает или задает стратегию поиска.
    /// </summary>
    public ElementSelectorStrategy Strategy { get; init; }

    /// <summary>
    /// Получает или задает строковое значение селектора.
    /// </summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>
    /// Создает селектор по CSS-выражению.
    /// </summary>
    public static ElementSelector Css(string value) => new(ElementSelectorStrategy.Css, value);

    /// <summary>
    /// Создает селектор по XPath-выражению.
    /// </summary>
    public static ElementSelector XPath(string value) => new(ElementSelectorStrategy.XPath, value);

    /// <summary>
    /// Создает селектор по идентификатору элемента.
    /// </summary>
    public static ElementSelector Id(string value) => new(ElementSelectorStrategy.Id, value);

    /// <summary>
    /// Создает селектор по текстовому содержимому.
    /// </summary>
    public static ElementSelector Text(string value) => new(ElementSelectorStrategy.Text, value);

    /// <summary>
    /// Создает селектор по атрибуту имени.
    /// </summary>
    public static ElementSelector Name(string value) => new(ElementSelectorStrategy.Name, value);

    /// <summary>
    /// Создает селектор по имени тега.
    /// </summary>
    public static ElementSelector TagName(string value) => new(ElementSelectorStrategy.TagName, value);
}