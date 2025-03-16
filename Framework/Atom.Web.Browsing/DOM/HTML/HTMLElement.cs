using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет элемент HTML.
/// </summary>
public class HTMLElement : Element, IHTMLElement
{
    /// <inheritdoc/>
    [ScriptMember]
    public string Title { get; set; }

    /// <inheritdoc/>
    [ScriptMember("lang")]
    public string Language { get; set; }

    /// <inheritdoc/>
    [ScriptMember("translate")]
    public bool IsTranslated { get; set; }

    /// <inheritdoc/>
    [ScriptMember("dir")]
    public string Direction { get; set; }

    /// <inheritdoc/>
    [ScriptMember("hidden")]
    public bool IsHidden { get; set; }

    /// <inheritdoc/>
    [ScriptMember("inert")]
    public bool IsInert { get; set; }

    /// <inheritdoc/>
    [ScriptMember]
    public string AccessKey { get; set; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string AccessKeyLabel { get; }

    /// <inheritdoc/>
    [ScriptMember("draggable")]
    public bool IsDraggable { get; set; }

    /// <inheritdoc/>
    [ScriptMember("spellcheck")]
    public bool IsSpellCheck { get; set; }

    /// <inheritdoc/>
    [ScriptMember]
    public string WritingSuggestions { get; set; }

    /// <inheritdoc/>
    [ScriptMember]
    public string AutoCapitalize { get; set; }

    /// <inheritdoc/>
    [ScriptMember("autocorrect")]
    public bool IsAutoCorrect { get; set; }

    internal HTMLElement(Uri namespaceURI, string localName) : base(namespaceURI, localName, NodeType.Element)
    {
        Title = string.Empty;
        Language = string.Empty;
        Direction = string.Empty;
        AccessKey = string.Empty;
        AccessKeyLabel = string.Empty;
        WritingSuggestions = string.Empty;
        AutoCapitalize = string.Empty;
    }

    /// <inheritdoc/>
    [ScriptMember]
    public void Click() => throw new NotImplementedException();
}