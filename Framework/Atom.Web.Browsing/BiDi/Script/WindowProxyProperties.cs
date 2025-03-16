using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Object representing the properties of a window proxy object.
/// </summary>
public class WindowProxyProperties
{

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowProxyProperties"/> class.
    /// </summary>
    [JsonConstructor]
    internal WindowProxyProperties() { }

    /// <summary>
    /// Gets the browsing context ID for the window proxy.
    /// </summary>
    [JsonPropertyName("context")]
    [JsonRequired]
    [JsonInclude]
    public string Context { get; internal set; } = string.Empty;
}