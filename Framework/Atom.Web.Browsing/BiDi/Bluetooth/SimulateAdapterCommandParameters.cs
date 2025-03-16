using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Bluetooth;

/// <summary>
/// Представляет параметры для команды bluetooth.simulateAdapter.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="SimulateAdapterCommandParameters"/>.
/// </remarks>
/// <param name="browsingContextId">Идентификатор контекста просмотра, для которого симулируется Bluetooth-адаптер.</param>
/// <param name="state">Симулированное состояние Bluetooth-адаптера.</param>
public class SimulateAdapterCommandParameters(string browsingContextId, AdapterState state) : CommandParameters<EmptyResult>
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "bluetooth.simulateAdapter";

    /// <summary>
    /// Идентификатор контекста просмотра, для которого симулируется Bluetooth-адаптер.
    /// </summary>
    [JsonPropertyName("context")]
    public string BrowsingContextId { get; set; } = browsingContextId;

    /// <summary>
    /// Симулированное состояние Bluetooth-адаптера.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public AdapterState State { get; set; } = state;
}