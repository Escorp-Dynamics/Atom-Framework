using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Интерфейс, реализуемый объектами, которые могут генерировать события и могут иметь подписчиков на эти события.
/// </summary>
public interface IEventTarget
{
    /// <summary>
    /// Регистрирует обработчик событий указанного типа на объекте.
    /// </summary>
    /// <param name="type">Тип события.</param>
    /// <param name="callback">Обработчик события.</param>
    /// <param name="options">Настройки добавления события.</param>
    [ScriptMember]
    void AddEventListener(string type, IEventListener callback, AddEventListenerOptions options);

    /// <summary>
    /// Регистрирует обработчик событий указанного типа на объекте.
    /// </summary>
    /// <param name="type">Тип события.</param>
    /// <param name="callback">Обработчик события.</param>
    [ScriptMember]
    void AddEventListener(string type, IEventListener callback) => AddEventListener(type, callback, AddEventListenerOptions.Default);

    /// <summary>
    /// Регистрирует обработчик событий указанного типа на объекте.
    /// </summary>
    /// <param name="type">Тип события.</param>
    /// <param name="callback">Обработчик события.</param>
    /// <param name="options">Настройки добавления события.</param>
    [ScriptMember]
    void AddEventListener(string type, Func<IEvent, bool> callback, AddEventListenerOptions options) => AddEventListener(type, new EventListener(e => callback(e)), options);

    /// <summary>
    /// Регистрирует обработчик событий указанного типа на объекте.
    /// </summary>
    /// <param name="type">Тип события.</param>
    /// <param name="callback">Обработчик события.</param>
    [ScriptMember]
    void AddEventListener(string type, Func<IEvent, bool> callback) => AddEventListener(type, callback, AddEventListenerOptions.Default);

    /// <summary>
    /// Регистрирует обработчик событий указанного типа на объекте.
    /// </summary>
    /// <param name="type">Тип события.</param>
    /// <param name="callback">Обработчик события.</param>
    /// <param name="options">Настройки добавления события.</param>
    [ScriptMember]
    void AddEventListener(string type, Action<IEvent> callback, AddEventListenerOptions options) => AddEventListener(type, new EventListener(callback), options);

    /// <summary>
    /// Регистрирует обработчик событий указанного типа на объекте.
    /// </summary>
    /// <param name="type">Тип события.</param>
    /// <param name="callback">Обработчик события.</param>
    [ScriptMember]
    void AddEventListener(string type, Action<IEvent> callback) => AddEventListener(type, callback, AddEventListenerOptions.Default);

    /// <summary>
    /// Удаляет обработчик события.
    /// </summary>
    /// <param name="type">Тип события.</param>
    /// <param name="callback">Обработчик события.</param>
    /// <param name="options">Настройки добавления события.</param>
    [ScriptMember]
    void RemoveEventListener(string type, IEventListener callback, EventListenerOptions options);

    /// <summary>
    /// Удаляет обработчик события.
    /// </summary>
    /// <param name="type">Тип события.</param>
    /// <param name="callback">Обработчик события.</param>
    [ScriptMember]
    void RemoveEventListener(string type, IEventListener callback) => RemoveEventListener(type, callback, EventListenerOptions.Default);

    /// <summary>
    /// Удаляет обработчик события.
    /// </summary>
    /// <param name="type">Тип события.</param>
    /// <param name="callback">Обработчик события.</param>
    /// <param name="options">Настройки добавления события.</param>
    [ScriptMember]
    void RemoveEventListener(string type, Func<IEvent, bool> callback, EventListenerOptions options) => RemoveEventListener(type, new EventListener(e => callback(e)), options);

    /// <summary>
    /// Удаляет обработчик события.
    /// </summary>
    /// <param name="type">Тип события.</param>
    /// <param name="callback">Обработчик события.</param>
    [ScriptMember]
    void RemoveEventListener(string type, Func<IEvent, bool> callback) => RemoveEventListener(type, callback, EventListenerOptions.Default);

    /// <summary>
    /// Удаляет обработчик события.
    /// </summary>
    /// <param name="type">Тип события.</param>
    /// <param name="callback">Обработчик события.</param>
    /// <param name="options">Настройки добавления события.</param>
    [ScriptMember]
    void RemoveEventListener(string type, Action<IEvent> callback, EventListenerOptions options) => RemoveEventListener(type, new EventListener(callback), options);

    /// <summary>
    /// Удаляет обработчик события.
    /// </summary>
    /// <param name="type">Тип события.</param>
    /// <param name="callback">Обработчик события.</param>
    [ScriptMember]
    void RemoveEventListener(string type, Action<IEvent> callback) => RemoveEventListener(type, callback, EventListenerOptions.Default);

    /// <summary>
    /// Генерирует событие на объекте <see cref="IEventTarget"/>.
    /// </summary>
    /// <param name="event">Параметры события.</param>
    [ScriptMember]
    bool DispatchEvent(IEvent @event);
}