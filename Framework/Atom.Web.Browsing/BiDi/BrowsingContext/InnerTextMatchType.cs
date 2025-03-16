using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Тип соответствия внутреннему тексту для локатора поиска.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<InnerTextMatchType>))]
public enum InnerTextMatchType
{
    /// <summary>
    /// Локатор сопоставляет подстроку внутреннего текста.
    /// </summary>
    Partial,
    /// <summary>
    /// Локатор сопоставляет полное значение внутреннего текста.
    /// </summary>
    Full,
}