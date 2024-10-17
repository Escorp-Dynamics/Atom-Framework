using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет инструкцию со связанной целью.
/// </summary>
public class ProcessingInstruction : CharacterData, IProcessingInstruction
{
    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string Target { get; }

    internal ProcessingInstruction(Uri baseURI, string name, string data, string target) : base(baseURI, name, data) => Target = target;
}