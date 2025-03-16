using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет базовый интерфейс для реализации слотируемых элементов.
/// </summary>
public interface ISlottable
{
    /// <summary>
    /// Назначенный слот.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    IHTMLSlotElement? AssignedSlot { get; }
}