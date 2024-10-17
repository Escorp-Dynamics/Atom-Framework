using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Реализует интерфейс <see cref="ISVGAnimatedString"/> для работы с анимированными строками в SVG.
/// </summary>
public class SVGAnimatedString : ISVGAnimatedString
{
    private string baseVal;

    /// <inheritdoc/>
    [ScriptMember("baseVal")]
    public string Base
    {
        get => baseVal;

        set
        {
            baseVal = value;
            Anim = value;    // TODO: Реализовать алгоритм обновления.
        }
    }

    /// <inheritdoc/>
    [ScriptMember("animVal", ScriptAccess.ReadOnly)]
    public string Anim { get; private set; }

    internal SVGAnimatedString(string initialBaseVal) => baseVal = Anim = initialBaseVal;

    internal SVGAnimatedString() : this(string.Empty) { }
}