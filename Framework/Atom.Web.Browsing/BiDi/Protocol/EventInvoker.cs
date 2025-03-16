namespace Atom.Web.Browsing.BiDi.Protocol;

/// <summary>
/// Object containing data about a WebDriver Bidi event.
/// </summary>
public abstract class EventInvoker
{
    /// <summary>
    /// Asynchronously invokes the event.
    /// </summary>
    /// <param name="eventData">The data to use when invoking the event.</param>
    /// <param name="additionalData">Additional data passed to the event for invocation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="BiDiException">
    /// Thrown when the type of the event data is not the type associated with this event data class.
    /// </exception>
    public abstract ValueTask InvokeEventAsync(object eventData, ReceivedDataDictionary additionalData);
}

/// <summary>
/// Object containing data about a WebDriver Bidi event where the data type is specifically known.
/// </summary>
/// <typeparam name="T">The type of the data for the event.</typeparam>
/// <remarks>
/// Initializes a new instance of the <see cref="EventInvoker{T}"/> class.
/// </remarks>
/// <param name="asyncInvokerDelegate">The asynchronous delegate to use when invoking the event.</param>
public class EventInvoker<T>(Func<EventInfo<T>, ValueTask> asyncInvokerDelegate) : EventInvoker
{
    private readonly Func<EventInfo<T>, ValueTask> asyncInvokerDelegate = asyncInvokerDelegate;

    /// <summary>
    /// Asynchronously invokes the event.
    /// </summary>
    /// <param name="eventData">The data to use when invoking the event.</param>
    /// <param name="additionalData">Additional data passed to the event for invocation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="BiDiException">
    /// Thrown when the type of the event data is not the type associated with this event data class.
    /// </exception>
    public override async ValueTask InvokeEventAsync(object eventData, ReceivedDataDictionary additionalData)
    {
        if (eventData is not T typedEventData) throw new BiDiException($"Unable to cast received event data to {typeof(T)}");

        EventInfo<T> invocationData = new(typedEventData, additionalData);
        await asyncInvokerDelegate(invocationData).ConfigureAwait(false);
    }
}