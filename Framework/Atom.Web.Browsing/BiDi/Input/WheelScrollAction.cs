using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет действие для прокрутки колеса устройства.
/// </summary>
public class WheelScrollAction : IWheelSourceAction
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
    public string Type { get; } = "scroll";

    /// <summary>
    /// Горизонтальная позиция действия.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ulong X { get; set; }

    /// <summary>
    /// Вертикальная позиция действия.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ulong Y { get; set; }

    /// <summary>
    /// Горизонтальное изменение для действия.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long DeltaX { get; set; }

    /// <summary>
    /// Вертикальное изменение для действия.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long DeltaY { get; set; }

    /// <summary>
    /// Длительность перемещения в миллисекундах.
    /// </summary>
    [JsonIgnore]
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// Источник перемещения.
    /// </summary>
    [JsonIgnore]
    public Origin? Origin { get; set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WheelScrollAction"/>.
    /// </summary>
    public WheelScrollAction() : base() { }
}