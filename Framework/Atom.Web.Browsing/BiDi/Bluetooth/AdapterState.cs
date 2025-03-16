using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Bluetooth;

/// <summary>
/// Состояние адаптера Bluetooth.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<AdapterState>))]
public enum AdapterState
{
    /// <summary>
    /// Адаптер Bluetooth отсутствует.
    /// </summary>
    Absent,
    /// <summary>
    /// Адаптер Bluetooth присутствует, но выключен.
    /// </summary>
    [JsonEnumValue("powered-off")]
    PoweredOff,
    /// <summary>
    /// Адаптер Bluetooth присутствует и включён.
    /// </summary>
    [JsonEnumValue("powered-on")]
    PoweredOn,
}