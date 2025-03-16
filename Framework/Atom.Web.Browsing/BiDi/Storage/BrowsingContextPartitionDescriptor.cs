using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Storage;

/// <summary>
/// Object containing a descriptor for a partition key for a browser cookie using a browsing context.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="BrowsingContextPartitionDescriptor"/> class.
/// </remarks>
/// <param name="browsingContextId">The ID of the browsing context for this partition key descriptor.</param>
public class BrowsingContextPartitionDescriptor(string browsingContextId) : PartitionDescriptor()
{
    private readonly string type = "context";

    /// <summary>
    /// Gets the type of the partition key descriptor.
    /// </summary>
    [JsonPropertyName("type")]
    public override string Type => type;

    /// <summary>
    /// Gets or sets the ID of the browsing context for this partition key descriptor.
    /// </summary>
    [JsonPropertyName("context")]
    public string BrowsingContextId { get; set; } = browsingContextId;
}