using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

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