using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// A header from a request.
/// </summary>
public class CookieHeader
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CookieHeader"/> class.
    /// </summary>
    [JsonConstructor]
    public CookieHeader() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="CookieHeader"/> class with the specified name and string value.
    /// </summary>
    /// <param name="name">The name of the header.</param>
    /// <param name="value">The string value of the header.</param>
    public CookieHeader(string name, string value)
    {
        Name = name;
        Value = new BytesValue(BytesValueType.String, value);
    }

    /// <summary>
    /// Gets or sets the name of the header.
    /// </summary>
    [JsonPropertyName("name")]
    [JsonRequired]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the value of the header.
    /// </summary>
    [JsonPropertyName("value")]
    [JsonRequired]
    public BytesValue Value { get; set; } = new(BytesValueType.String, string.Empty);
}