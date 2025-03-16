using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Storage;

/// <summary>
/// Provides parameters for the storage.deleteCookies command.
/// </summary>
public class DeleteCookiesCommandParameters : CommandParameters<DeleteCookiesCommandResult>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteCookiesCommandParameters"/> class.
    /// </summary>
    public DeleteCookiesCommandParameters() { }

    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "storage.deleteCookies";

    /// <summary>
    /// Gets or sets the filter to use when getting the cookies.
    /// </summary>
    [JsonPropertyName("filter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public CookieFilter? Filter { get; set; }

    /// <summary>
    /// Gets or sets the partition descriptor to use when getting the cookies.
    /// </summary>
    [JsonPropertyName("partition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public PartitionDescriptor? Partition { get; set; }
}