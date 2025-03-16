using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет тип документа.
/// </summary>
public interface IDocumentType : INode, IChildNode
{
    /// <summary>
    /// Публичный идентификатор.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    string PublicId { get; }

    /// <summary>
    /// Системный идентификатор.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    string SystemId { get; }
}