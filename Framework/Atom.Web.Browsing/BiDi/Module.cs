using System.Text.Json.Serialization.Metadata;
using Atom.Web.Browsing.BiDi.Protocol;

namespace Atom.Web.Browsing.BiDi;

/// <summary>
/// Представляет модуль в протоколе WebDriver Bidi.
/// </summary>
public abstract class Module
{
    private readonly Dictionary<string, EventInvoker> asyncEventInvokers = [];

    /// <summary>
    /// Драйвер, используемый для связи модуля.
    /// </summary>
    protected BiDiDriver Driver { get; }

    /// <summary>
    /// Имя модуля.
    /// </summary>
    public abstract string ModuleName { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Module"/>.
    /// </summary>
    /// <param name="driver">Драйвер, используемый для связи модуля.</param>
    protected Module(BiDiDriver driver)
    {
        Driver = driver;
        Driver.OnEventReceived.AddObserver(OnDriverEventReceived);
    }

    private async ValueTask OnDriverEventReceived(EventReceivedEventArgs e)
    {
        if (asyncEventInvokers.TryGetValue(e.EventName, out var eventInvoker))
            await eventInvoker.InvokeEventAsync(e.EventData!, e.AdditionalData).ConfigureAwait(false);
    }

    /// <summary>
    /// Регистрирует обработчик для указанного события.
    /// </summary>
    /// <typeparam name="T">Тип данных, используемых в событии.</typeparam>
    /// <param name="eventName">Имя события.</param>
    /// <param name="typeInfo">Информация о типе.</param>
    /// <param name="eventInvoker">Делегат, принимающий один параметр типа T, используемый для вызова события.</param>
    protected virtual void RegisterAsyncEventInvoker<T>(string eventName, JsonTypeInfo<EventMessage<T>> typeInfo, Func<EventInfo<T>, ValueTask> eventInvoker)
    {
        asyncEventInvokers[eventName] = new EventInvoker<T>(eventInvoker);
        Driver.RegisterEvent(eventName, typeInfo);
    }
}