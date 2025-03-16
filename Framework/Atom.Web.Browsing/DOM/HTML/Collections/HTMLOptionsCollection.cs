using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет коллекцию опций формы.
/// </summary>
public class HTMLOptionsCollection : HTMLCollection, IHTMLOptionsCollection
{
    /// <inheritdoc/>
    [ScriptMember]
    public new IElement? this[int index]
    {
        get => Elements.ElementAtOrDefault(index);

        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Elements.Count);

            Elements[index] = value;
        }
    }

    /// <inheritdoc/>
    [ScriptMember]
    public new int Length
    {
        get => Elements.Count;

        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            while (Elements.Count > value) Elements.RemoveAt(Elements.Count - 1);
            while (Elements.Count < value) Elements.Add(default);
        }
    }

    /// <inheritdoc/>
    [ScriptMember]
    public int SelectedIndex
    {
        get;

        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, -1);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value, Elements.Count);

            field = value;
        }
    } = -1;

    /// <inheritdoc/>
    [ScriptMember]
    public void Add(IHTMLElement element, int beforeIndex)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(beforeIndex, -1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(beforeIndex, Elements.Count);

        if (beforeIndex is -1)
            Elements.Add(element);
        else
            Elements.Insert(beforeIndex, element);
    }

    /// <inheritdoc/>
    [ScriptMember]
    public void Add(IHTMLElement element, IHTMLElement? before)
    {
        var index = before is not null ? Elements.IndexOf(before) : -1;
        Add(element, index);
    }

    /// <inheritdoc/>
    [ScriptMember]
    public void Add(IHTMLElement element) => Add(element, -1);

    /// <inheritdoc/>
    [ScriptMember]
    public void Remove(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Elements.Count);

        Elements.RemoveAt(index);
    }
}