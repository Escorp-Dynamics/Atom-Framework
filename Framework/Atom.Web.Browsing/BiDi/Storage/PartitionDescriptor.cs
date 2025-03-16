using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Storage;

/// <summary>
/// Object containing a descriptor for a partition key for a browser cookie using a browsing context.
/// </summary>
[JsonDerivedType(typeof(BrowsingContextPartitionDescriptor))]
[JsonDerivedType(typeof(StorageKeyPartitionDescriptor))]
public abstract class PartitionDescriptor
{
    /// <summary>
    /// Gets the type of the partition key descriptor.
    /// </summary>
    [JsonPropertyName("type")]
    public abstract string Type { get; }
}