using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет список переключателей формы.
/// </summary>
public interface IRadioNodeList : INodeList
{
    /// <summary>
    /// Значение переключателя.
    /// </summary>
    [ScriptMember]
    string Value { get; set; }
}