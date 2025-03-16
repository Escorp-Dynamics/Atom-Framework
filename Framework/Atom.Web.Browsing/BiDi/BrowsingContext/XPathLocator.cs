namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет локатор для поиска узлов с помощью XPath.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="XPathLocator"/>.
/// </remarks>
/// <param name="value">XPath, используемый для поиска узлов.</param>
public class XPathLocator(string value) : Locator()
{
    private readonly string type = "xpath";
    private readonly string value = value;

    /// <summary>
    /// Тип локатора.
    /// </summary>
    public override string Type => type;

    /// <summary>
    /// XPath, используемый для поиска узлов, для целей сериализации.
    /// </summary>
    public override object Value => value;
}