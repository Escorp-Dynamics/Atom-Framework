using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет кастомное событие.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="CustomEvent"/>.
/// </remarks>
/// <param name="type">Тип события.</param>
/// <param name="eventInitDict">Свойства инициализации.</param>
public class CustomEvent(string type, CustomEventInit eventInitDict) : Event(type, eventInitDict), ICustomEvent
{
    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public object? Detail { get; } = eventInitDict.Detail;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="CustomEvent"/>.
    /// </summary>
    /// <param name="type">Тип события.</param>
    public CustomEvent(string type) : this(type, CustomEventInit.Default) { }

    /// <inheritdoc/>
    [ScriptMember("initCustomEvent")]
    public void Init(string type, bool isBubbles, bool isCancelable, object? detail) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember("initCustomEvent")]
    public void Init(string type, bool isBubbles, bool isCancelable) => Init(type, isBubbles, isCancelable, default);

    /// <inheritdoc/>
    [ScriptMember("initCustomEvent")]
    public void Init(string type, bool isBubbles) => Init(type, isBubbles, default);

    /// <inheritdoc/>
    [ScriptMember("initCustomEvent")]
    public void Init(string type) => Init(type, default);
}