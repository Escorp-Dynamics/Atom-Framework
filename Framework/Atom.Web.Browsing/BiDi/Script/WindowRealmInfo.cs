using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Object representing a window realm for executing script.
/// </summary>
public class WindowRealmInfo : RealmInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WindowRealmInfo"/> class.
    /// </summary>
    internal WindowRealmInfo() : base() { }

    /// <summary>
    /// Gets the ID of the browsing context containing this window realm.
    /// </summary>
    [JsonPropertyName("context")]
    [JsonRequired]
    public string BrowsingContext { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the sandbox name for the realm.
    /// </summary>
    [JsonPropertyName("sandbox")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sandbox { get; internal set; }
}