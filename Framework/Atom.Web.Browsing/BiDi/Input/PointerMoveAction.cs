using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет действие для отправки перемещения указателя на устройстве указателя.
/// </summary>
public class PointerMoveAction : PointerAction, IPointerSourceAction
{
    [JsonPropertyName("duration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    internal ulong? SerializableDuration => Duration is null ? null : Convert.ToUInt64(Duration.Value.TotalMilliseconds);

    [JsonPropertyName("origin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    internal object? SerializableOrigin => Origin?.Value;

    /// <summary>
    /// Тип действия.
    /// </summary>
    public string Type { get; } = "pointerMove";

    /// <summary>
    /// Горизонтальное расстояние перемещения, измеряемое в пикселях от точки начала.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long X { get; set; }

    /// <summary>
    /// Вертикальное расстояние перемещения, измеряемое в пикселях от точки начала.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long Y { get; set; }

    /// <summary>
    /// Длительность перемещения.
    /// </summary>
    [JsonIgnore]
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// Источник перемещения.
    /// </summary>
    [JsonIgnore]
    public Origin? Origin { get; set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="PointerMoveAction"/>.
    /// </summary>
    public PointerMoveAction() : base() { }
}