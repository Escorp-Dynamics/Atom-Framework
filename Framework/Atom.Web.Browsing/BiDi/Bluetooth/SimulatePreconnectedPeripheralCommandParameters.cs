using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Bluetooth;

/// <summary>
/// Представляет параметры для команды bluetooth.simulatePreconnectedPeripheral.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="SimulatePreConnectedPeripheralCommandParameters"/>.
/// </remarks>
/// <param name="browsingContextId">Идентификатор контекста просмотра, для которого симулируется уже подключённое Bluetooth-периферийное устройство.</param>
/// <param name="address">Адрес Bluetooth-периферийного устройства.</param>
/// <param name="name">Имя Bluetooth-периферийного устройства.</param>
public class SimulatePreConnectedPeripheralCommandParameters(string browsingContextId, string address, string name) : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "bluetooth.simulatePreconnectedPeripheral";

    /// <summary>
    /// Идентификатор контекста просмотра, для которого симулируется уже подключённое Bluetooth-периферийное устройство.
    /// </summary>
    [JsonPropertyName("context")]
    public string BrowsingContextId { get; set; } = browsingContextId;

    /// <summary>
    /// Адрес симулированного уже подключённого Bluetooth-периферийного устройства.
    /// </summary>
    public string Address { get; set; } = address;

    /// <summary>
    /// Отображаемое имя симулированного уже подключённого Bluetooth-периферийного устройства.
    /// </summary>
    public string Name { get; set; } = name;

    /// <summary>
    /// Список всех данных производителя для симулированного уже подключённого Bluetooth-периферийного устройства.
    /// </summary>
    public IEnumerable<BluetoothManufacturerData> ManufacturerData { get; set; } = [];

    /// <summary>
    /// Список всех известных UUID сервисов для симулированного уже подключённого Bluetooth-периферийного устройства.
    /// </summary>
    [JsonPropertyName("knownServiceUuids")]
    public IEnumerable<string> KnownServiceUUIDs { get; set; } = [];
}