using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Начало координат скриншота.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<ScreenshotOrigin>))]
public enum ScreenshotOrigin
{
    /// <summary>
    /// Начало координат прямоугольника обрезки относительно области просмотра браузера.
    /// </summary>
    Viewport,
    /// <summary>
    /// Начало координат прямоугольника обрезки относительно начала документа.
    /// </summary>
    Document,
}