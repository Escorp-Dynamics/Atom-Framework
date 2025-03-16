using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет действия с устройством ввода типа указателя.
/// </summary>
public class PointerSourceActions : SourceActions
{
    /// <summary>
    /// Тип действий источника.
    /// </summary>
    public override string Type => "pointer";

    /// <summary>
    /// Параметры для устройства указателя.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PointerParameters? Parameters { get; set; }

    /// <summary>
    /// Коллекция действий для этого устройства ввода.
    /// </summary>
    public IEnumerable<IPointerSourceAction> Actions { get; set; } = [];
}