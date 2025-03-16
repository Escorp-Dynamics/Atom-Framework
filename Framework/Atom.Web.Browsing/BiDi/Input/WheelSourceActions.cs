namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет действия с устройством ввода типа колеса.
/// </summary>
public class WheelSourceActions : SourceActions
{
    /// <summary>
    /// Тип действий источника.
    /// </summary>
    public override string Type => "wheel";

    /// <summary>
    /// Коллекция действий для этого устройства ввода.
    /// </summary>
    public IEnumerable<IWheelSourceAction> Actions { get; set; } = [];
}