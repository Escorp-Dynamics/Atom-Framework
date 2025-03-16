namespace Atom.Web.Browsing.BiDi.Input;

/// <summary>
/// Представляет действия с устройством ввода с клавиатуры.
/// </summary>
public class KeySourceActions : SourceActions
{
    /// <summary>
    /// Тип действий источника.
    /// </summary>
    public override string Type => "key";

    /// <summary>
    /// Коллекция действий для этого устройства ввода.
    /// </summary>
    public IEnumerable<IKeySourceAction> Actions { get; set; } = [];
}