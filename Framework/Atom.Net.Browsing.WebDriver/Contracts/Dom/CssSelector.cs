namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет специализированный CSS-селектор.
/// </summary>
/// <param name="value">Строковое значение CSS-селектора.</param>
public sealed class CssSelector(string value) : ElementSelector(ElementSelectorStrategy.Css, value) { }