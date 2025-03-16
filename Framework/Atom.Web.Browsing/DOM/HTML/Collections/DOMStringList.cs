using System.Collections;
using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Реализация интерфейса <see cref="IDOMStringList"/>, который представляет собой список строк, используемых в DOM.
/// </summary>
public class DOMStringList : IDOMStringList
{
    /// <summary>
    /// Коллекция элементов.
    /// </summary>
    [ScriptMember(ScriptAccess.None)]
    protected IList<string> Elements { get; } = [];

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public int Length => Elements.Count;

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string? this[int index] => Elements.ElementAtOrDefault(index);

    internal DOMStringList(IEnumerable<string> items) => Elements = new List<string>(items);

    internal DOMStringList() : this([]) { }

    /// <inheritdoc/>
    [ScriptMember]
    public bool Contains(string value) => Elements.Contains(value);

    /// <summary>
    /// Возвращает перечислитель, который выполняет итерацию по коллекции строк.
    /// </summary>
    /// <returns>Перечислитель строк.</returns>
    public IEnumerator<string> GetEnumerator() => Elements.GetEnumerator();

    /// <summary>
    /// Возвращает перечислитель, который выполняет итерацию по коллекции строк.
    /// </summary>
    /// <returns>Перечислитель строк.</returns>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}