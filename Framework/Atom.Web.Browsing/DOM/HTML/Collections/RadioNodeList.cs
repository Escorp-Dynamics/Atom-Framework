using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет список переключателей формы.
/// </summary>
public class RadioNodeList : NodeList, IRadioNodeList
{
    /// <inheritdoc/>
    [ScriptMember]
    public string Value { get; set; } = string.Empty;

    internal RadioNodeList(IEnumerable<INode> nodes) : base(nodes) { }

    internal RadioNodeList() : this([]) { }
}