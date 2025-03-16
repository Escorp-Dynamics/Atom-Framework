using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет настройки добавления обработчика событий.
/// </summary>
public class AddEventListenerOptions : EventListenerOptions
{
    /// <summary>
    /// Определяет, находится ли обработчик в пассивном режиме.
    /// </summary>
    [ScriptMember("passive")]
    public bool IsPassive { get; set; }

    /// <summary>
    /// Определяет, будет ли обработчик выполнен только единожды.
    /// </summary>
    /// <value></value>
    [ScriptMember("once")]
    public bool IsOnce { get; set; }

    /// <summary>
    /// Сигнал отмены выполнения обработчика.
    /// </summary>
    [ScriptMember]
    public IAbortSignal? Signal { get; set; }

    [ScriptMember(ScriptAccess.None)]
    internal static new AddEventListenerOptions Default { get; } = new();
}