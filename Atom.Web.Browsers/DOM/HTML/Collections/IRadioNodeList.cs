using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

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