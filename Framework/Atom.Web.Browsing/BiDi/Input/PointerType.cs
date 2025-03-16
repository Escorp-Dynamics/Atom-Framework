using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Тип устройства указателя.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<PointerType>))]
public enum PointerType
{
    /// <summary>
    /// Устройство указателя — мышь.
    /// </summary>
    Mouse,
    /// <summary>
    /// Устройство указателя — стилус, похожий на ручку.
    /// </summary>
    Pen,
    /// <summary>
    /// Устройство указателя — сенсорное устройство.
    /// </summary>
    Touch,
}