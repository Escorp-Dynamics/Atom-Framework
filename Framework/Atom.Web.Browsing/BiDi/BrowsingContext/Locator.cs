using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет локатор для поиска узлов.
/// </summary>
[JsonDerivedType(typeof(AccessibilityLocator))]
[JsonDerivedType(typeof(ContextLocator))]
[JsonDerivedType(typeof(CssLocator))]
[JsonDerivedType(typeof(InnerTextLocator))]
[JsonDerivedType(typeof(XPathLocator))]
public abstract class Locator
{
    /// <summary>
    /// Тип локатора.
    /// </summary>
    public abstract string Type { get; }

    /// <summary>
    /// Значение, используемое для поиска узлов.
    /// </summary>
    public abstract object Value { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Locator"/>.
    /// </summary>
    protected Locator() { }
}