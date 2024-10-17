using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет наблюдатель мутации.
/// </summary>
public interface IMutationObserver
{
    /// <summary>
    /// Наблюдает за мутацией элемента.
    /// </summary>
    /// <param name="target">Цель наблюдения.</param>
    /// <param name="options">Свойства наблюдения.</param>
    [ScriptMember]
    void Observe(INode target, MutationObserverInit options);

    /// <summary>
    /// Наблюдает за мутацией элемента.
    /// </summary>
    /// <param name="target">Цель наблюдения.</param>
    [ScriptMember]
    void Observe(INode target) => Observe(target, MutationObserverInit.Default);

    /// <summary>
    /// Прекращает наблюдение.
    /// </summary>
    [ScriptMember]
    void Disconnect();

    /// <summary>
    /// Возвращает записи мутации.
    /// </summary>
    [ScriptMember]
    IEnumerable<IMutationRecord> TakeRecords();
}