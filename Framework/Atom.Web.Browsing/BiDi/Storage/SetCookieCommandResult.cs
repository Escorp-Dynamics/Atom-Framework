using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Storage;

/// <summary>
/// Result for setting a cookie using the storage.setCookie command.
/// </summary>
public class SetCookieCommandResult : CommandResult
{
    [JsonConstructor]
    internal SetCookieCommandResult() { }

    /// <summary>
    /// Gets the partition key for the list of returned cookies.
    /// </summary>
    [JsonPropertyName("partition")]
    [JsonRequired]
    [JsonInclude]
    public PartitionKey Partition { get; internal set; } = new();
}