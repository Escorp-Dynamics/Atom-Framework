using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.WebExtension;

/// <summary>
/// Represents data describing a browser extension.
/// </summary>
[JsonDerivedType(typeof(ExtensionArchivePath))]
[JsonDerivedType(typeof(ExtensionBase64Encoded))]
[JsonDerivedType(typeof(ExtensionPath))]
public abstract class ExtensionData
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExtensionData"/> class.
    /// </summary>
    protected ExtensionData() { }

    /// <summary>
    /// Gets the type of extension data this item represents.
    /// </summary>
    [JsonPropertyName("type")]
    public abstract string Type { get; }
}