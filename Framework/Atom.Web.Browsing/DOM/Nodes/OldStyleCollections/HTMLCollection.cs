using System.Collections;
using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет именованную коллекцию элементов HTML.
/// </summary>
public class HTMLCollection : IHTMLCollection
{
    /// <summary>
    /// Коллекция элементов.
    /// </summary>
    [ScriptMember(ScriptAccess.None)]
    protected IList<IElement?> Elements { get; } = [];

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public int Length => Elements.Count;

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IElement? this[int index] => Elements.ElementAtOrDefault(index);

    internal HTMLCollection(IEnumerable<IElement?> items) => Elements = new List<IElement?>(items);

    internal HTMLCollection() : this([]) { }

    /// <inheritdoc/>
    [ScriptMember]
    public IElement? NamedItem(string name)
    {
        foreach (var element in Elements)
        {
            if (element is null) continue;
            if (element.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return element;
        }

        return default;
    }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.None)]
    internal void Add(IElement? element) => Elements.Add(element);

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.None)]
    public IEnumerator<IElement?> GetEnumerator() => Elements.GetEnumerator();

    [ScriptMember(ScriptAccess.None)]
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}