using System.Diagnostics.CodeAnalysis;

namespace Atom.Web.Browsing.BiDi.Protocol;

/// <summary>
/// Object containing event data for events raised when a protocol event is received from a WebDriver Bidi connection.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="EventReceivedEventArgs"/> class.
/// </remarks>
/// <param name="message">The event message containing information about the event.</param>
public class EventReceivedEventArgs([NotNull] EventMessage message) : EventArgs
{

    /// <summary>
    /// Gets the name of the event.
    /// </summary>
    public string EventName { get; } = message.EventName;

    /// <summary>
    /// Gets the data associated with the event.
    /// </summary>
    public object? EventData { get; } = message.EventData;

    /// <summary>
    /// Gets additional properties deserialized by this event.
    /// </summary>
    public ReceivedDataDictionary AdditionalData { get; } = message.AdditionalData;
}