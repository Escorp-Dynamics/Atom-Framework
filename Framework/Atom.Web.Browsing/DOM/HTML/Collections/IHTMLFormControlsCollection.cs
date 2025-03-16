using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет коллекцию элементов управления формы.
/// </summary>
public interface IHTMLFormControlsCollection : IHTMLCollection
{
    /// <summary>
    /// Возвращает <see cref="IRadioNodeList"/> или <see cref="IElement"/> по его имени.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    object? this[string name] { get; }

    /// <summary>
    /// Возвращает <see cref="IRadioNodeList"/> или <see cref="IElement"/> по его имени.
    /// </summary>
    /// <param name="name">Имя элемента.</param>
    [ScriptMember]
    new object? NamedItem(string name);
}