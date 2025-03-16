using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Bluetooth;

/// <summary>
/// Представляет информацию о записи, полученной при сканировании Bluetooth-устройств.
/// </summary>
public class ScanRecord
{
    /// <summary>
    /// Локальное имя Bluetooth-устройства или его префикс.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>
    /// Список UUID сервисов, которые, согласно этой записи, поддерживаются GATT-сервером Bluetooth-устройства.
    /// </summary>
    [JsonPropertyName("uuids")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<string>? UUIDs { get; set; }

    /// <summary>
    /// Внешний вид Bluetooth-устройства.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? Appearance { get; set; }

    /// <summary>
    /// Список данных производителя, связанных с Bluetooth-устройством.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<BluetoothManufacturerData>? ManufacturerData { get; set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ScanRecord"/>.
    /// </summary>
    public ScanRecord() { }
}