using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Storage;

/// <summary>
/// Result for deleting cookies using the storage.deleteCookies command.
/// </summary>
public class DeleteCookiesCommandResult : CommandResult
{
    [JsonConstructor]
    internal DeleteCookiesCommandResult() { }

    /// <summary>
    /// Gets the partition key for the list of returned cookies.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public PartitionKey Partition { get; internal set; } = new();
}