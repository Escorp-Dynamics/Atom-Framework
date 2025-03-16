using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Ориентация страницы при печати контекстов просмотра.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<PrintOrientation>))]
public enum PrintOrientation
{
    /// <summary>
    /// Портретная ориентация.
    /// </summary>
    Portrait,
    /// <summary>
    /// Альбомная ориентация.
    /// </summary>
    Landscape,
}