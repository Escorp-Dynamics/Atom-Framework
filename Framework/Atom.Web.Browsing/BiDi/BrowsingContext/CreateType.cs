using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Тип создания новых контекстов просмотра.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<CreateType>))]
public enum CreateType
{
    /// <summary>
    /// Создать контекст просмотра в новой вкладке.
    /// </summary>
    Tab,
    /// <summary>
    /// Создать контекст просмотра в новом окне.
    /// </summary>
    Window,
}