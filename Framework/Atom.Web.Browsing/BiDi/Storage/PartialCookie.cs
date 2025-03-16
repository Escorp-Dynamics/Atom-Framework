using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.Network;

namespace Atom.Web.Browsing.BiDi.Storage;

/// <summary>
/// Object containing a data for setting values for cookies.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PartialCookie"/> class.
/// </remarks>
/// <param name="name">The name of the cookie to set.</param>
/// <param name="value">The value of the cookie to set.</param>
/// <param name="domain">The domain of the cookie to set.</param>
public class PartialCookie(string name, BytesValue value, string domain)
{
    /// <summary>
    /// Gets or sets the name to use in querying for cookies.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = name;

    /// <summary>
    /// Gets or sets the value to use in querying for cookies.
    /// </summary>
    [JsonPropertyName("value")]
    public BytesValue Value { get; set; } = value;

    /// <summary>
    /// Gets or sets the domain to use in querying for cookies.
    /// </summary>
    [JsonPropertyName("domain")]
    public string Domain { get; set; } = domain;

    /// <summary>
    /// Gets or sets the path to use in querying for cookies.
    /// </summary>
    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets the byte length of the cookie when serialized in an HTTP cookie header
    /// to use in querying for cookies.
    /// </summary>
    [JsonPropertyName("size")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? Size { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the cookie is only available via HTTP headers
    /// to use in querying for cookies.
    /// </summary>
    [JsonPropertyName("httpOnly")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HttpOnly { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the cookie is secure, delivered via an
    /// encrypted connection like HTTPS to use in querying for cookies.
    /// </summary>
    [JsonPropertyName("secure")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Secure { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the cookie a same site cookie to use in querying for cookies.
    /// </summary>
    [JsonPropertyName("sameSite")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CookieSameSiteValue? SameSite { get; set; }

    /// <summary>
    /// Gets or sets the expiration time of the cookie for querying cookies.
    /// </summary>
    [JsonIgnore]
    public DateTime? Expires
    {
        get => EpochExpires.HasValue ? DateTime.UnixEpoch.AddMilliseconds(EpochExpires.Value) : null;
        set => EpochExpires = value.HasValue ? Convert.ToUInt64((value.Value - DateTime.UnixEpoch).TotalMilliseconds) : null;
    }

    /// <summary>
    /// Gets the dictionary containing extra data to use in querying for cookies.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object?> AdditionalData { get; } = [];

    /// <summary>
    /// Gets or sets the expiration time of the cookie as the total number of milliseconds
    /// elapsed since the start of the Unix epoch (1 January 1970 12:00AM UTC) to use in
    /// querying for cookies.
    /// </summary>
    [JsonPropertyName("expiry")]
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal ulong? EpochExpires { get; set; }
}