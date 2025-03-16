using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Browser;

/// <summary>
/// Представляет параметры для команды browser.setClientWindowState.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="SetClientWindowStateCommandParameters"/>.
/// </remarks>
/// <param name="clientWindowId">Идентификатор клиентского окна, для которого устанавливается состояние.</param>
public class SetClientWindowStateCommandParameters(string clientWindowId) : CommandParameters<SetClientWindowStateCommandResult>
{
    /// <summary>
    /// Имя метода команды.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "browser.setClientWindowState";

    /// <summary>
    /// Идентификатор клиентского окна, для которого устанавливается состояние.
    /// </summary>
    [JsonPropertyName("clientWindow")]
    [JsonInclude]
    public string ClientWindowId { get; set; } = clientWindowId;

    /// <summary>
    /// Состояние клиентского окна.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ClientWindowState State { get; set; }

    /// <summary>
    /// Значение в CSS-пикселях для левого края клиентского окна. Этот параметр игнорируется, если свойство <see cref="State"/> установлено в значение, отличное от <see cref="ClientWindowState.Normal"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? X
    {
        get => State is ClientWindowState.Normal ? field : null;
        set;
    }

    /// <summary>
    /// Значение в CSS-пикселях для верхнего края клиентского окна. Этот параметр игнорируется, если свойство <see cref="State"/> установлено в значение, отличное от <see cref="ClientWindowState.Normal"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? Y
    {
        get => State is ClientWindowState.Normal ? field : null;
        set;
    }

    /// <summary>
    /// Значение в CSS-пикселях для ширины клиентского окна. Этот параметр игнорируется, если свойство <see cref="State"/> установлено в значение, отличное от <see cref="ClientWindowState.Normal"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? Width
    {
        get => State is ClientWindowState.Normal ? field : null;
        set;
    }

    /// <summary>
    /// Значение в CSS-пикселях для высоты клиентского окна. Этот параметр игнорируется, если свойство <see cref="State"/> установлено в значение, отличное от <see cref="ClientWindowState.Normal"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? Height
    {
        get => State is ClientWindowState.Normal ? field : null;
        set;
    }
}