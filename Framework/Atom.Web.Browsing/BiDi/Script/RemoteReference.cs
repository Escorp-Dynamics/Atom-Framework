using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Object containing a remote reference.
/// </summary>
public class RemoteReference : ArgumentValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteReference"/> class.
    /// </summary>
    /// <param name="handle">The handle of the remote object.</param>
    /// <param name="sharedId">The shared ID of the remote object.</param>
    protected internal RemoteReference(string? handle, string? sharedId)
    {
        InternalHandle = handle;
        InternalSharedId = sharedId;
    }

    /// <summary>
    /// Gets the dictionary of additional data about the remote reference.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object?> AdditionalData { get; } = [];

    /// <summary>
    /// Gets or sets the internally accessible handle of the remote reference.
    /// </summary>
    [JsonPropertyName("handle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    protected internal string? InternalHandle { get; set; }

    /// <summary>
    /// Gets or sets the internally accessible shared ID of the remote reference.
    /// </summary>
    [JsonPropertyName("sharedId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    protected internal string? InternalSharedId { get; set; }
}