using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет опции получения корневого узла.
/// </summary>
public class GetRootNodeOptions
{
    /// <summary>
    /// Определяет, может или нет событие всплывать через границы между shadow DOM (внутренний DOM конкретного элемента) и обычного DOM документа.
    /// </summary>
    /// <value></value>
    [ScriptMember("composed")]
    public bool IsComposed { get; set; }

    [ScriptMember(ScriptAccess.None)]
    internal static GetRootNodeOptions Default { get; } = new();
}