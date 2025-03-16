namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет локатор для поиска узлов с использованием CSS-селектора.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="CssLocator"/>.
/// </remarks>
/// <param name="value">CSS-селектор для поиска узлов.</param>
public class CssLocator(string value) : Locator()
{
    private readonly string type = "css";
    private readonly string value = value;

    /// <summary>
    /// Тип локатора.
    /// </summary>
    public override string Type => type;

    /// <summary>
    /// CSS-селектор для поиска узлов.
    /// </summary>
    public override object Value => value;
}