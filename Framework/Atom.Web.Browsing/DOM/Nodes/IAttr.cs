using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет узел атрибута.
/// </summary>
public interface IAttr : INode
{
    /// <summary>
    /// Адрес пространства имён.
    /// </summary>
    [ScriptMember("namespaceURI", ScriptAccess.ReadOnly)]
    new Uri Uri { get; }

    /// <summary>
    /// Префикс пространства имён.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    string? Prefix { get; }

    /// <summary>
    /// Локальное название.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    string LocalName { get; }

    /// <summary>
    /// Значение.
    /// </summary>
    [ScriptMember]
    string Value { get; set; }

    /// <summary>
    /// Элемент, к которому привязан атрибут.
    /// </summary>
    [ScriptMember("ownerElement", ScriptAccess.ReadOnly)]
    IElement? Owner { get; }

    /// <summary>
    /// Указывает, является ли атрибут специфическим.
    /// </summary>
    [ScriptMember("specified", ScriptAccess.ReadOnly)]
    bool IsSpecified { get; }
}