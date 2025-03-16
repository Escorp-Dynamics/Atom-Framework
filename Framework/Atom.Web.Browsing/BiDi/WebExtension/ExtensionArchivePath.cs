using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.WebExtension;

/// <summary>
/// Represents a browser extension packaged inside a zip archive.
/// </summary>
public class ExtensionArchivePath : ExtensionData
{
    private readonly string extensionType = "archivePath";

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtensionArchivePath"/> class.
    /// </summary>
    public ExtensionArchivePath() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtensionArchivePath"/> class.
    /// </summary>
    /// <param name="path">The full path and file name to the zip archive file containing the extension.</param>
    public ExtensionArchivePath(string path) => Path = path;

    /// <summary>
    /// Gets the type of extension data this item represents.
    /// </summary>
    [JsonPropertyName("type")]
    public override string Type => extensionType;

    /// <summary>
    /// Gets or sets the full path and file name to the zip archive file containing the extension.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}