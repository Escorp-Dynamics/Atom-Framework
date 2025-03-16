using System.Collections.ObjectModel;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Provides a read-only dictionary of attributes for a node.
/// </summary>
public class NodeAttributes : ReadOnlyDictionary<string, string>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NodeAttributes"/> class.
    /// </summary>
    /// <param name="dictionary">The dictionary to wrap as a read-only construct.</param>
    internal NodeAttributes(Dictionary<string, string> dictionary) : base(dictionary) { }
}