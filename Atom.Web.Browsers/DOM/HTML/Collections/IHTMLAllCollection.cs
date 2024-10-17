using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет коллекцию HTML-элементов.
/// </summary>
public interface IHTMLAllCollection : IHTMLCollection
{
    /// <summary>
    /// Возвращает <see cref="IHTMLCollection"/> или <see cref="IElement"/> по его имени.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    object? this[string name] { get; }

    /// <summary>
    /// Возвращает <see cref="IHTMLCollection"/> или <see cref="IElement"/> по его имени.
    /// </summary>
    /// <param name="name">Имя элемента.</param>
    [ScriptMember]
    new object? NamedItem(string name);
}