using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Protocol;

/// <summary>
/// Base class for the result of a command.
/// </summary>
public class CommandResponseMessage : Message
{
    /// <summary>
    /// Gets the ID for this command during execution.
    /// </summary>
    [JsonPropertyName("id")]
    [JsonInclude]
    [JsonRequired]
    public long Id { get; internal set; }

    /// <summary>
    /// Gets the result data for the command.
    /// </summary>
    [JsonIgnore]
    public virtual CommandResult Result => SerializableResult!;

    /// <summary>
    /// Gets the result of the command for serialization purposes.
    /// </summary>
    [JsonPropertyName("result")]
    [JsonRequired]
    [JsonInclude]
    internal CommandResult? SerializableResult { get; set; }
}

/// <summary>
/// Base class for the result of a command where the concrete type of the response data is known.
/// </summary>
/// <typeparam name="T">The data type of the command response.</typeparam>
public class CommandResponseMessage<T> : CommandResponseMessage where T : CommandResult
{
    /// <summary>
    /// Gets the result of the command.
    /// </summary>
    [JsonIgnore]
    public override CommandResult Result => SerializableResult!;

    /// <summary>
    /// Gets the result of the command for serialization purposes.
    /// </summary>
    [JsonPropertyName("result")]
    [JsonRequired]
    [JsonInclude]
    internal new T? SerializableResult { get; set; }
}