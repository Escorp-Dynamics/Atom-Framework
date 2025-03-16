using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Browser;

/// <summary>
/// Представляет данные об окне, в котором находится браузер.
/// </summary>
public class ClientWindowInfo
{
    /// <summary>
    /// Идентификатор клиентского окна.
    /// </summary>
    [JsonPropertyName("clientWindow")]
    [JsonInclude]
    [JsonRequired]
    public string ClientWindowId { get; internal set; } = string.Empty;

    /// <summary>
    /// Указывает, активно ли клиентское окно, обычно подразумевая, что оно имеет фокус в операционной системе.
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

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ClientWindowInfo"/>.
    /// </summary>
    [JsonConstructor]
    internal ClientWindowInfo() { }
}