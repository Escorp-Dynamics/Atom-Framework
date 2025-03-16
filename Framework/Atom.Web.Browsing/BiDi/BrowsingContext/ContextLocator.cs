using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.BrowsingContext;

/// <summary>
/// Represents a locator for locating context nodes of given browsing contexts like, for example, the iframe element
/// hosting a BrowsingContext in the iframe.
/// </summary>
public class ContextLocator : Locator
{
    private readonly string type = "context";
    private readonly Dictionary<string, string> contextAttributes = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ContextLocator"/> class.
    /// </summary>
    /// <param name="browsingContextId">The ID of the browsing context for which to locate the context node.</param>
    public ContextLocator(string browsingContextId) : base() => contextAttributes["context"] = browsingContextId;

    /// <summary>
    /// Gets the type of locator.
    /// </summary>
    public override string Type => type;

    /// <summary>
    /// Gets a read-only version of a dictionary containing the accessibility attributes to use in locating nodes.
    /// </summary>
    [JsonPropertyName("value")]
    public override object Value => new ReadOnlyDictionary<string, string>(contextAttributes);

    /// <summary>
    /// Gets the browsing context for which to get the context node..
    /// </summary>
    [JsonIgnore]
    public string BrowsingContextId => contextAttributes["context"];
}