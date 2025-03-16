using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Storage;

/// <summary>
/// Provides parameters for the storage.setCookie command.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="SetCookieCommandParameters"/> class.
/// </remarks>
/// <param name="cookie">The values of the cookie to set.</param>
public class SetCookieCommandParameters(PartialCookie cookie) : CommandParameters<SetCookieCommandResult>
{
    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonIgnore]
    public override string MethodName => "storage.setCookie";

    /// <summary>
    /// Gets or sets the filter to use when getting the cookies.
    /// </summary>
    [JsonPropertyName("cookie")]
    public PartialCookie Cookie { get; set; } = cookie;

    /// <summary>
    /// Gets or sets the partition descriptor to use when getting the cookies.
    /// </summary>
    [JsonPropertyName("partition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public PartitionDescriptor? Partition { get; set; }
}