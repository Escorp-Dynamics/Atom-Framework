using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Bluetooth;

/// <summary>
/// Представляет параметры для команды bluetooth.simulateAdvertisement.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="SimulateAdvertisementCommandParameters"/>.
/// </remarks>
/// <param name="browsingContextId">Идентификатор контекста просмотра, для которого симулируется реклама Bluetooth-периферийного устройства.</param>
/// <param name="scanEntry">Объект <see cref="SimulateAdvertisementScanEntry"/>, представляющий данные о сканировании периферийных устройств.</param>
public class SimulateAdvertisementCommandParameters(string browsingContextId, SimulateAdvertisementScanEntry scanEntry) : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "bluetooth.simulateAdvertisement";

    /// <summary>
    /// Идентификатор контекста просмотра, для которого симулируется реклама Bluetooth-периферийного устройства.
    /// </summary>
    [JsonPropertyName("context")]
    public string BrowsingContextId { get; set; } = browsingContextId;

    /// <summary>
    /// Данные о сканировании периферийных устройств.
    /// </summary>
    public SimulateAdvertisementScanEntry ScanEntry { get; set; } = scanEntry;
}