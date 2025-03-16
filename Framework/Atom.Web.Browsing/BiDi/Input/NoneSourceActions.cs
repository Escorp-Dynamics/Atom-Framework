namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет действия без устройства ввода.
/// </summary>
public class NoneSourceActions : SourceActions
{
    /// <summary>
    /// Тип действий источника.
    /// </summary>
    public override string Type => "none";

    /// <summary>
    /// Коллекция действий для этого устройства ввода.
    /// </summary>
    public IEnumerable<INoneSourceAction> Actions { get; set; } = [];
}