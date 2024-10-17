using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет текстовый узел.
/// </summary>
public interface IText : ICharacterData, ISlottable
{
    /// <summary>
    /// Исходная строка.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    string WholeText { get; }

    /// <summary>
    /// Разделяет текст по указанному индексу.
    /// </summary>
    /// <param name="offset">Смещение.</param>
    /// <returns>Экземпляр отделённого текстового узла.</returns>
    [ScriptMember("splitText")]
    IText Split(int offset);
}