using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Bluetooth;

/// <summary>
/// Представляет информацию о записи, полученной при сканировании Bluetooth-устройств.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="SimulateAdvertisementScanEntry"/>.
/// </remarks>
/// <param name="deviceAddress">Физический адрес симулированного Bluetooth-периферийного устройства.</param>
/// <param name="receivedSignalStrengthIndicator">Симулированная сила сигнала, выраженная в dBm.</param>
/// <param name="scanRecord">Объект <see cref="ScanRecord"/>, представляющий данные о сканировании Bluetooth-устройств.</param>
public class SimulateAdvertisementScanEntry(string deviceAddress, double receivedSignalStrengthIndicator, ScanRecord scanRecord)
{
    /// <summary>
    /// Физический адрес симулированного Bluetooth-устройства или его префикс.
    /// </summary>
    [JsonPropertyName("address")]
    public string DeviceAddress { get; set; } = deviceAddress;

    /// <summary>
    /// Симулированная сила сигнала, выраженную в dBm.
    /// </summary>
    public double RSSI { get; set; } = receivedSignalStrengthIndicator;

    /// <summary>
    /// Данные о сканировании Bluetooth-устройств.
    /// </summary>
    public ScanRecord ScanRecord { get; set; } = scanRecord;
}