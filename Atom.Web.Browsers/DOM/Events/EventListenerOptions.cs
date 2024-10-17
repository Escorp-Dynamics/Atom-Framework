using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Настройки обработчика событий.
/// </summary>
public class EventListenerOptions
{
    /// <summary>
    /// Определяет, происходит ли захват события.
    /// </summary>
    [ScriptMember("capture")]
    public bool IsCaptured { get; set; }

    [ScriptMember(ScriptAccess.None)]
    internal static EventListenerOptions Default { get; } = new();
}