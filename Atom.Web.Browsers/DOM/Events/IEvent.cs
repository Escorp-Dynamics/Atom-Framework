using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет собой любое событие, которое происходит в DOM.
/// </summary>
public interface IEvent
{
    /// <summary>
    /// События, которые в данный момент не отправлены, находятся в этой фазе.
    /// </summary>
    [ScriptMember("NONE", ScriptAccess.ReadOnly)]
    public const ushort None = 0;

    /// <summary>
    /// Когда событие отправляется объекту, участвующему в дереве, оно будет находиться в этой фазе до того, как достигнет своей цели.
    /// </summary>
    [ScriptMember("CAPTURING_PHASE", ScriptAccess.ReadOnly)]
    public const ushort CapturingPhase = 1;

    /// <summary>
    /// Когда событие будет отправлено, оно будет находиться в этой фазе на своем целевом объекте.
    /// </summary>
    [ScriptMember("AT_TARGET", ScriptAccess.ReadOnly)]
    public const ushort AtTarget = 2;

    /// <summary>
    /// Когда событие отправляется объекту, участвующему в дереве, оно будет находиться в этой фазе после того, как достигнет своей цели.
    /// </summary>
    [ScriptMember("BUBBLING_PHASE", ScriptAccess.ReadOnly)]
    public const ushort BubblingPhase = 3;

    /// <summary>
    /// Тип события.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    string Type { get; }

    /// <summary>
    /// Ссылка на целевой объект, на котором произошло событие.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    IEventTarget? Target { get; }

    /// <summary>
    /// Ссылка на текущий зарегистрированный объект, на котором обрабатывается событие.
    /// Это объект, которому планируется отправка события; поведение можно изменить с использованием перенаправления (retargeting).
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    IEventTarget? CurrentTarget { get; }

    /// <summary>
    /// Указывает фазу процесса обработки события.
    /// </summary>
    [ScriptMember("eventPhase", ScriptAccess.ReadOnly)]
    ushort Phase { get; }

    /// <summary>
    /// Указывает, всплыло ли событие вверх по DOM или нет.
    /// </summary>
    [ScriptMember("bubbles", ScriptAccess.ReadOnly)]
    bool IsBubbles { get; }

    /// <summary>
    /// Указывает на возможность отмены события.
    /// </summary>
    [ScriptMember("cancelable", ScriptAccess.ReadOnly)]
    bool IsCancelable { get; }

    /// <summary>
    /// Показывает, была ли для события вызвана функция <see cref="PreventDefault"/>.
    /// </summary>
    [ScriptMember("defaultPrevented", ScriptAccess.ReadOnly)]
    bool IsDefaultPrevented { get; }

    /// <summary>
    /// Указывает, может или нет событие всплывать через границы между shadow DOM (внутренний DOM конкретного элемента) и обычного DOM документа.
    /// </summary>
    [ScriptMember("composed", ScriptAccess.ReadOnly)]
    bool IsComposed { get; }

    /// <summary>
    /// Указывает, было или нет событие инициировано браузером (например, по клику мышью)
    /// или из скрипта (например, через функцию создания события).
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    bool IsTrusted { get; }

    /// <summary>
    /// Время, когда событие было создано (в миллисекундах).
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    DateTimeOffset TimeStamp { get; }

    /// <summary>
    /// Возвращает целевые объекты вызова пути события (объекты, для которых будут вызываться прослушиватели),
    /// за исключением любых узлов в теневых деревьях, для которых режим теневого корня "closed", которые недоступны из <see cref="CurrentTarget"/> события.
    /// </summary>
    [ScriptMember]
    IEnumerable<IEventTarget> ComposedPath();

    /// <summary>
    /// Остановка распространения события далее по DOM.
    /// </summary>
    [ScriptMember]
    void StopPropagation();

    /// <summary>
    /// Для конкретного события не будет больше вызвано обработчиков.
    /// Ни тех, которые привязаны к этому же элементу (на котором работает обработчик, который вызывает этот <see cref="StopImmediatePropagation"/>),
    /// ни других, которые могли бы вызваться при распространении события позже (например, в фазе перехвата - capture).
    /// </summary>
    [ScriptMember]
    void StopImmediatePropagation();

    /// <summary>
    /// Отменяет событие (если его возможно отменить).
    /// </summary>
    [ScriptMember]
    void PreventDefault();
}