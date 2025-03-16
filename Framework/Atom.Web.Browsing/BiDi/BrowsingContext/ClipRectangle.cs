using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Представляет прямоугольник обрезки для скриншота.
/// </summary>
[JsonDerivedType(typeof(BoxClipRectangle))]
[JsonDerivedType(typeof(ElementClipRectangle))]
public abstract class ClipRectangle
{
    /// <summary>
    /// Тип прямоугольника обрезки.
    /// </summary>
    public abstract string Type { get; }
}