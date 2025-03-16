using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.WebExtension;

/// <summary>
/// Represents a browser extension packaged inside a zip archive encoded as a base64-encoded string.
/// </summary>
public class ExtensionBase64Encoded : ExtensionData
{
    private readonly string extensionType = "base64";

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtensionBase64Encoded"/> class.
    /// </summary>
    public ExtensionBase64Encoded() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtensionBase64Encoded"/> class.
    /// </summary>
    /// <param name="extensionValue">A web extension zip archive represented as a base64-encoded string.</param>
    public ExtensionBase64Encoded(string extensionValue) => Value = extensionValue;

    /// <summary>
    /// Gets the type of extension data this item represents.
    /// </summary>
    [JsonPropertyName("type")]
    public override string Type => extensionType;

    /// <summary>
    /// Gets or sets a web extension zip archive represented as a base64-encoded string..
    /// </summary>
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}