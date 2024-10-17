using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет коллекцию опций формы.
/// </summary>
public interface IHTMLOptionsCollection : IHTMLCollection
{
    /// <summary>
    /// Возвращает элемент по его индексу.
    /// </summary>
    [ScriptMember]
    new IElement? this[int index] { get; set; }

    /// <summary>
    /// Количество элементов в коллекции.
    /// </summary>
    [ScriptMember]
    new int Length { get; set; }

    /// <summary>
    /// Индекс выбранной опции.
    /// </summary>
    [ScriptMember]
    int SelectedIndex { get; set; }

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="element"></param>
    /// <param name="before"></param>
    void Add(IHTMLElement element, IHTMLElement? before);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="element"></param>
    /// <param name="beforeIndex"></param>
    void Add(IHTMLElement element, int beforeIndex);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="element"></param>
    /// <returns></returns>
    void Add(IHTMLElement element) => Add(element, -1);

    /// <summary>
    /// TODO.
    /// </summary>
    /// <param name="index"></param>
    void Remove(int index);
}