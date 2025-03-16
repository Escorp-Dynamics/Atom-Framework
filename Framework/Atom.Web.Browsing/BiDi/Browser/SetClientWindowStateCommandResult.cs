using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Browser;

/// <summary>
/// Представляет результат получения текущих клиентских окон для команды browser.setClientWindowState.
/// </summary>
/// <remarks>
/// Этот класс результата по сути является копией <see cref="ClientWindowInfo"/>, так как именно этот тип указан в протоколе. Это сделано таким образом, потому что команды должны возвращать тип, производный от <see cref="CommandResult"/>, а C# не поддерживает множественное наследование.
/// Если структура объекта информации изменится, этот класс потребует обновлений для соответствия.
/// </remarks>
public class SetClientWindowStateCommandResult : CommandResult
{
    /// <summary>
    /// Идентификатор клиентского окна.
    /// </summary>
    [JsonPropertyName("clientWindow")]
    [JsonInclude]
    [JsonRequired]
    public string ClientWindowId { get; internal set; } = string.Empty;

    /// <summary>
    /// Определяет, активно ли клиентское окно, обычно подразумевая, что оно имеет фокус в операционной системе.
    /// </summary>
    [JsonPropertyName("active")]
    [JsonInclude]
    [JsonRequired]
    public bool IsActive { get; internal set; }

    /// <summary>
    /// Состояние клиентского окна.
    /// </summary>
    [JsonInclude]
    [JsonRequired]
    public ClientWindowState State { get; internal set; }

    /// <summary>
    /// Значение в CSS-пикселях для левого края клиентского окна.
    /// </summary>
    [JsonInclude]
    [JsonRequired]
    public ulong X { get; internal set; }

    /// <summary>
    /// Значение в CSS-пикселях для верхнего края клиентского окна.
    /// </summary>
    [JsonInclude]
    [JsonRequired]
    public ulong Y { get; internal set; }

    /// <summary>
    /// Значение в CSS-пикселях для ширины клиентского окна.
    /// </summary>
    [JsonInclude]
    [JsonRequired]
    public ulong Width { get; internal set; }

    /// <summary>
    /// Значение в CSS-пикселях для высоты клиентского окна.
    /// </summary>
    [JsonInclude]
    [JsonRequired]
    public ulong Height { get; internal set; }

    [JsonConstructor]
    internal SetClientWindowStateCommandResult() { }
}