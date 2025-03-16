using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет действие для приостановки выполнения устройства.
/// </summary>
public class PauseAction : INoneSourceAction, IKeySourceAction, IPointerSourceAction, IWheelSourceAction
{
    [JsonPropertyName("duration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    internal ulong? SerializedDuration => !Duration.HasValue ? null : Convert.ToUInt64(Duration.Value.TotalMilliseconds);

    /// <summary>
    /// Тип действия.
    /// </summary>
    public string Type { get; } = "pause";

    /// <summary>
    /// Длительность паузы.
    /// </summary>
    [JsonIgnore]
    public TimeSpan? Duration { get; set; }
}