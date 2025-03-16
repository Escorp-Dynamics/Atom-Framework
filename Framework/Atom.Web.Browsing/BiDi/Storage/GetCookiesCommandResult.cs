using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.Network;

namespace Atom.Web.Browsing.BiDi.Storage;

/// <summary>
/// Result for getting cookies using the storage.getCookies command.
/// </summary>
public class GetCookiesCommandResult : CommandResult
{
    [JsonConstructor]
    internal GetCookiesCommandResult() { }

    /// <summary>
    /// Gets the read-only list of cookies returned by the command.
    /// </summary>
    [JsonIgnore]
    public IList<Cookie> Cookies => SerializableCookies.AsReadOnly();

    /// <summary>
    /// Gets the partition key for the list of returned cookies.
    /// </summary>
    [JsonPropertyName("partition")]
    [JsonRequired]
    [JsonInclude]
    public PartitionKey Partition { get; internal set; } = new();

    /// <summary>
    /// Gets or sets the list of cookies returned by the command for serialization purposes.
    /// </summary>
    [JsonPropertyName("cookies")]
    [JsonRequired]
    [JsonInclude]
    internal List<Cookie> SerializableCookies { get; set; } = [];
}