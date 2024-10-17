using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Интерфейс для работы с анимированными строками в SVG.
/// </summary>
public interface ISVGAnimatedString
{
    /// <summary>
    /// Базовое значение анимированной строки.
    /// </summary>
    [ScriptMember("baseVal")]
    string Base { get; set; }

    /// <summary>
    /// Текущее анимированное значение строки.
    /// </summary>
    [ScriptMember("animVal", ScriptAccess.ReadOnly)]
    string Anim { get; }
}