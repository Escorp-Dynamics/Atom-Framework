using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Свойства инициализации кастомного события.
/// </summary>
public class CustomEventInit : EventInit
{
    /// <summary>
    /// Детали события.
    /// </summary>
    [ScriptMember]
    public object? Detail { get; set; }

    /// <summary>
    /// Свойства инициализации по умолчанию.
    /// </summary>
    [ScriptMember(ScriptAccess.None)]
    public static new CustomEventInit Default { get; } = new CustomEventInit();
}