using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет узел атрибута.
/// </summary>
public class Attr : Node, IAttr
{
    /// <inheritdoc/>
    [ScriptMember("namespaceURI", ScriptAccess.ReadOnly)]
    public new Uri Uri { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string? Prefix { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string LocalName { get; }

    /// <inheritdoc/>
    [ScriptMember]
    public string Value { get; set; }

    /// <inheritdoc/>
    [ScriptMember("ownerElement", ScriptAccess.ReadOnly)]
    public IElement? Owner { get; internal set; }

    /// <inheritdoc/>
    [ScriptMember("specified", ScriptAccess.ReadOnly)]
    public bool IsSpecified { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Attr"/>.
    /// </summary>
    /// <param name="namespaceURI">Адрес пространства имён.</param>
    /// <param name="prefix">Префикс пространства имён.</param>
    /// <param name="localName">Локальное название.</param>
    /// <param name="name">Название.</param>
    /// <param name="value">Значение.</param>
    internal Attr(Uri namespaceURI, string prefix, string localName, string name, string value) : base(namespaceURI, name, NodeType.Attribute)
    {
        Uri = namespaceURI;
        Prefix = prefix;
        LocalName = localName;
        Value = value;
        IsSpecified = true;
    }
}