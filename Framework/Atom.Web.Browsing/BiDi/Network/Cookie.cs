using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// Represents a cookie in a web request or response.
/// </summary>
public class Cookie
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Cookie"/> class.
    /// </summary>
    [JsonConstructor]
    internal Cookie() { }

    /// <summary>
    /// Gets the name of the cookie.
    /// </summary>
    [JsonPropertyName("name")]
    [JsonRequired]
    [JsonInclude]
    public string Name { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the value of the cookie.
    /// </summary>
    [JsonPropertyName("value")]
    [JsonRequired]
    [JsonInclude]
    public BytesValue Value { get; internal set; } = new(BytesValueType.String, string.Empty);

    /// <summary>
    /// Gets the domain of the cookie.
    /// </summary>
    [JsonPropertyName("domain")]
    [JsonRequired]
    [JsonInclude]
    public string Domain { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the path of the cookie.
    /// </summary>
    [JsonPropertyName("path")]
    [JsonRequired]
    [JsonInclude]
    public string Path { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the expiration time of the cookie.
    /// </summary>
    [JsonIgnore]
    public DateTime? Expires { get; internal set; }

    /// <summary>
    /// Gets the expiration time of the cookie as the total number of milliseconds
    /// elapsed since the start of the Unix epoch (1 January 1970 12:00AM UTC).
    /// </summary>
    [JsonPropertyName("expiry")]
    [JsonInclude]
    public ulong? EpochExpires
    {
        get;

        internal set
        {
            field = value;
            if (value.HasValue) Expires = DateTime.UnixEpoch.AddMilliseconds(value.Value);
        }
    }

    /// <summary>
    /// Gets the byte length of the cookie when serialized in an HTTP cookie header.
    /// </summary>
    [JsonPropertyName("size")]
    [JsonRequired]
    [JsonInclude]
    public long Size { get; internal set; } = 0;

    /// <summary>
    /// Gets a value indicating whether the cookie is secure, delivered via an
    /// encrypted connection like HTTPS.
    /// </summary>
    [JsonPropertyName("secure")]
    [JsonRequired]
    [JsonInclude]
    public bool Secure { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the cookie is only available via HTTP headers
    /// (<see langword="true"/>), or if the cookie can be inspected and manipulated
    /// via JavaScript (<see langword="false"/>).
    /// </summary>
    [JsonPropertyName("httpOnly")]
    [JsonRequired]
    [JsonInclude]
    public bool HttpOnly { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the cookie a same site cookie.
    /// </summary>
    [JsonPropertyName("sameSite")]
    [JsonRequired]
    [JsonInclude]
    public CookieSameSiteValue SameSite { get; internal set; } = CookieSameSiteValue.None;

    /// <summary>
    /// Converts this cookie to a <see cref="SetCookieHeader"/>.
    /// </summary>
    /// <returns>The SetCookieHeader representing this cookie.</returns>
    public SetCookieHeader ToSetCookieHeader() => new(this);
}