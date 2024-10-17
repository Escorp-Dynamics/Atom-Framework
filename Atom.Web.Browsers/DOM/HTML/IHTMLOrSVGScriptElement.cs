using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// TODO.
/// </summary>
[ScriptUsage(ScriptAccess.None)]
public interface IHTMLOrSVGScriptElement
{
    /// <summary>
    /// .
    /// </summary>
    /// <value></value>
    string Name { get; }    // FIXME: Удалить.
}