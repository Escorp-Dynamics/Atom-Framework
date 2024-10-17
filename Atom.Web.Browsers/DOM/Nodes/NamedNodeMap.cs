using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет именованный маппинг узлов.
/// </summary>
public class NamedNodeMap : INamedNodeMap
{
    private readonly List<IAttr> attributes = [];

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public int Length => attributes.Count;

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IAttr? this[int index] => attributes.ElementAtOrDefault(index);

    /// <inheritdoc/>
    [ScriptMember("getNamedItem")]
    public IAttr? Get(string qualifiedName)
    {
        foreach (var attr in this) if (attr.Name.Equals(qualifiedName, StringComparison.OrdinalIgnoreCase)) return attr;
        return default;
    }

    /// <inheritdoc/>
    [ScriptMember("getNamedItemNS")]
    public IAttr? Get(Uri? namespaceURI, string localName)
    {
        foreach (var attr in this) if (attr.Name.Equals(localName, StringComparison.OrdinalIgnoreCase) && attr.Uri == namespaceURI) return attr;
        return default;
    }

    /// <inheritdoc/>
    [ScriptMember("setNamedItem")]
    public IAttr? Set([NotNull] IAttr attr)
    {
        var existingAttr = Get(attr.Name);
        if (existingAttr is not null) attributes.Remove(existingAttr);

        attributes.Add(attr);
        return existingAttr;
    }

    /// <inheritdoc/>
    [ScriptMember("setNamedItemNS")]
    public IAttr? SetByNS([NotNull] IAttr attr)
    {
        var existingAttr = Get(attr.Uri, attr.LocalName);
        if (existingAttr is not null) attributes.Remove(existingAttr);

        attributes.Add(attr);
        return existingAttr;
    }

    /// <inheritdoc/>
    [ScriptMember("removeNamedItem")]
    public IAttr? Remove(string qualifiedName)
    {
        var attr = Get(qualifiedName);
        if (attr is not null) attributes.Remove(attr);
        return attr;
    }

    /// <inheritdoc/>
    [ScriptMember("removeNamedItemNS")]
    public IAttr? Remove(Uri? namespaceURI, string localName)
    {
        var attr = Get(namespaceURI, localName);
        if (attr is not null) attributes.Remove(attr);
        return attr;
    }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.None)]
    public IEnumerator<IAttr> GetEnumerator() => attributes.GetEnumerator();

    [ScriptMember(ScriptAccess.None)]
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}