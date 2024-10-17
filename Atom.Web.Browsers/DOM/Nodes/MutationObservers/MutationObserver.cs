using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Делегат наблюдения за мутацией.
/// </summary>
/// <param name="mutations">Коллекция мутаций.</param>
/// <param name="observer">Наблюдатель.</param>
public delegate void MutationCallback(IEnumerable<IMutationRecord> mutations, IMutationObserver observer);

/// <summary>
/// Представляет наблюдатель мутации.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="MutationObserver"/>.
/// </remarks>
/// <param name="callback">Делегат наблюдения за мутацией.</param>
public class MutationObserver(MutationCallback callback) : IMutationObserver
{
    private readonly MutationCallback callback = callback;
    private readonly List<IMutationRecord> records = [];

    /// <inheritdoc/>
    [ScriptMember]
    public void Observe(INode target, MutationObserverInit options) => callback([], this); // TODO: Доделать.

    /// <inheritdoc/>
    [ScriptMember]
    public void Observe(INode target) => Observe(target, MutationObserverInit.Default);

    /// <inheritdoc/>
    [ScriptMember]
    public void Disconnect()
    {
        // Реализация метода disconnect
        // Здесь должна быть логика для прекращения наблюдения
    }

    /// <inheritdoc/>
    [ScriptMember]
    public IEnumerable<IMutationRecord> TakeRecords()
    {
        // Реализация метода takeRecords
        var records = new List<IMutationRecord>(this.records);
        this.records.Clear();
        return records;
    }

    // Метод для добавления записей, которые будут возвращены при вызове TakeRecords
    //private void AddRecord(MutationRecord record) => records.Add(record);

    // Метод для вызова колбэка с накопленными записями
    /*private void Notify()
    {
        callback(records, this);
        records.Clear();
    }*/
}