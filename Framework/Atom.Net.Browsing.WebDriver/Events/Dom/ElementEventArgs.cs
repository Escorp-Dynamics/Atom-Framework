using System.Text.Json;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Содержит данные DOM-события, доставленного через element event-listener bridge.
/// </summary>
public sealed class ElementEventArgs : MutableEventArgs
{
    /// <summary>
    /// Получает исходный JSON payload события.
    /// </summary>
    public JsonElement Payload { get; init; }

    /// <summary>
    /// Получает имя DOM-события.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// Получает признак доверенного браузерного события.
    /// </summary>
    public bool IsTrusted { get; init; }

    /// <summary>
    /// Получает идентификатор целевого элемента события.
    /// </summary>
    public string? TargetId { get; init; }

    /// <summary>
    /// Получает идентификатор текущего элемента-обработчика.
    /// </summary>
    public string? CurrentTargetId { get; init; }

    /// <summary>
    /// Получает текущее значение элемента, если событие было доставлено от form-control.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    /// Получает значение KeyboardEvent.key, если оно доступно.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>
    /// Получает значение KeyboardEvent.code, если оно доступно.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// Получает индекс кнопки мыши, если событие содержит mouse/button payload.
    /// </summary>
    public int? Button { get; init; }

    /// <summary>
    /// Получает clientX события, если оно доступно.
    /// </summary>
    public double? ClientX { get; init; }

    /// <summary>
    /// Получает clientY события, если оно доступно.
    /// </summary>
    public double? ClientY { get; init; }

    internal static ElementEventArgs FromPayload(JsonElement payload)
    {
        var snapshot = payload.ValueKind == JsonValueKind.Undefined ? default : payload.Clone();

        return new ElementEventArgs
        {
            Payload = snapshot,
            Type = TryGetString(snapshot, "type"),
            IsTrusted = TryGetBoolean(snapshot, "isTrusted"),
            TargetId = TryGetString(snapshot, "targetId"),
            CurrentTargetId = TryGetString(snapshot, "currentTargetId"),
            Value = TryGetString(snapshot, "value"),
            Key = TryGetString(snapshot, "key"),
            Code = TryGetString(snapshot, "code"),
            Button = TryGetInt32(snapshot, "button"),
            ClientX = TryGetDouble(snapshot, "clientX"),
            ClientY = TryGetDouble(snapshot, "clientY"),
        };
    }

    private static string? TryGetString(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private static bool TryGetBoolean(JsonElement payload, string propertyName)
        => payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            && property.GetBoolean();

    private static int? TryGetInt32(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(propertyName, out var property))
            return null;

        return property.TryGetInt32(out var value) ? value : null;
    }

    private static double? TryGetDouble(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(propertyName, out var property))
            return null;

        return property.TryGetDouble(out var value) ? value : null;
    }
}