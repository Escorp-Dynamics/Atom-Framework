using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет коллекцию HTML-элементов.
/// </summary>
public class HTMLAllCollection : HTMLCollection, IHTMLAllCollection
{
    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public object? this[string name] => NamedItem(name);

    /// <inheritdoc/>
    [ScriptMember]
    public new object? NamedItem(string name)
    {
        IElement[] namedElements = [.. Elements.Where(e => e is not null && e.Name == name)];
        return namedElements.Length is 0 ? default : namedElements.Length is 1 ? namedElements[0] : new HTMLCollection(namedElements);
    }
}