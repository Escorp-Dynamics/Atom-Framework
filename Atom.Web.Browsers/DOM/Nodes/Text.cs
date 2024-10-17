using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет текстовый узел.
/// </summary>
public class Text : CharacterData, IText
{
    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string WholeText
    {
        get
        {
            var text = Data;
            var sibling = NextSibling;

            while (sibling is Text textNode)
            {
                text += textNode.Data;
                sibling = textNode.NextSibling;
            }

            return text;
        }
    }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IHTMLSlotElement? AssignedSlot { get; }

    internal Text(Uri baseURI, string data) : base(baseURI, "text", data) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Text"/>.
    /// </summary>
    /// <param name="data">Данные.</param>
    public Text(string data) : this(new Uri("about:blank"), data) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Text"/>.
    /// </summary>
    public Text() : this(string.Empty) { }

    /// <inheritdoc/>
    [ScriptMember("splitText")]
    public IText Split(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, Length);

        var remainingData = Substring(offset, Length - offset);
        Remove(offset, Length - offset);

        var newText = new Text(remainingData);
        Parent?.InsertBefore(newText, NextSibling);

        return newText;
    }
}