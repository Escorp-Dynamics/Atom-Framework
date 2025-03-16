using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Protocol;

/// <summary>
/// Deserializes a message that represents an event as defined by the WebDriver Bidi protocol.
/// </summary>
public abstract class EventMessage : Message
{
    /// <summary>
    /// Gets the name of the event.
    /// </summary>
    [JsonPropertyName("method")]
    [JsonInclude]
    public string EventName { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the data for the event.
    /// </summary>
    [JsonIgnore]
    public abstract object EventData { get; }
}

/// <summary>
/// Deserializes a message that represents an event as defined by the WebDriver Bidi protocol where the event data type is known.
/// </summary>
/// <typeparam name="T">The type of data contained in the event.</typeparam>
public class EventMessage<T> : EventMessage
{
    /// <summary>
    /// Gets the data associated with the event.
    /// </summary>
    [JsonIgnore]
    public override object EventData => SerializableData!;

    /// <summary>
    /// Gets the data of the event for serialization purposes.
    /// </summary>
    [JsonPropertyName("params")]
    [JsonRequired]
    [JsonInclude]
    internal T? SerializableData { get; set; }
}