using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет кастомное событие.
/// </summary>
public interface ICustomEvent : IEvent
{
    /// <summary>
    /// Детали события.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    object? Detail { get; }

    /// <summary>
    /// Инициализирует событие.
    /// </summary>
    /// <param name="type">Тип события.</param>
    /// <param name="isBubbles">Указывает, всплыло ли событие вверх по DOM или нет.</param>
    /// <param name="isCancelable">Указывает на возможность отмены события.</param>
    /// <param name="detail">Детали события.</param>
    [ScriptMember("initCustomEvent")]
    void Init(string type, bool isBubbles, bool isCancelable, object? detail);

    /// <summary>
    /// Инициализирует событие.
    /// </summary>
    /// <param name="type">Тип события.</param>
    /// <param name="isBubbles">Указывает, всплыло ли событие вверх по DOM или нет.</param>
    /// <param name="isCancelable">Указывает на возможность отмены события.</param>
    [ScriptMember("initCustomEvent")]
    void Init(string type, bool isBubbles, bool isCancelable) => Init(type, isBubbles, isCancelable, default);

    /// <summary>
    /// Инициализирует событие.
    /// </summary>
    /// <param name="type">Тип события.</param>
    /// <param name="isBubbles">Указывает, всплыло ли событие вверх по DOM или нет.</param>
    [ScriptMember("initCustomEvent")]
    void Init(string type, bool isBubbles) => Init(type, isBubbles, default);

    /// <summary>
    /// Инициализирует событие.
    /// </summary>
    /// <param name="type">Тип события.</param>
    [ScriptMember("initCustomEvent")]
    void Init(string type) => Init(type, default);
}