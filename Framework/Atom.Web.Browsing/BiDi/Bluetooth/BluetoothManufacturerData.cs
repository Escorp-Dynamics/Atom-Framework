using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Bluetooth;

/// <summary>
/// Представляет информацию о производителе Bluetooth-устройства.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="BluetoothManufacturerData"/>.
/// </remarks>
/// <param name="key">Уникальное целое число, определяющее код идентификатора компании производителя.</param>
/// <param name="data">Последовательность байтов данных, представляющая данные производителя в виде строки, закодированной в base64.</param>
public class BluetoothManufacturerData(uint key, string data)
{
    /// <summary>
    /// Код идентификатора компании производителя.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public uint Key { get; set; } = key;

    /// <summary>
    /// Последовательность байтов данных производителя в виде строки, закодированной в base64.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public string Data { get; set; } = data;
}