using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Свойства инициализации события.
/// </summary>
public class EventInit
{
    /// <summary>
    /// Определяет, всплыло ли событие вверх по DOM или нет.
    /// </summary>
    [ScriptMember("bubbles")]
    public bool IsBubbles { get; set; }

    /// <summary>
    /// Определяет, может ли событие быть отменено.
    /// </summary>
    [ScriptMember("cancelable")]
    public bool IsCancelable { get; set; }

    /// <summary>
    /// Определяет, может или нет событие всплывать через границы между shadow DOM (внутренний DOM конкретного элемента) и обычного DOM документа.
    /// </summary>
    /// <value></value>
    [ScriptMember("composed")]
    public bool IsComposed { get; set; }

    [ScriptMember(ScriptAccess.None)]
    internal static EventInit Default { get; } = new();
}