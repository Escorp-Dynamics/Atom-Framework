using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет инструкцию со связанной целью.
/// </summary>
public interface IProcessingInstruction : ICharacterData
{
    /// <summary>
    /// Связанная цель.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    string Target { get; }
}