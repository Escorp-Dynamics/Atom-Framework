using System.Text;
using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет собой объект <see cref="INode"/>, который содержит символы.
/// </summary>
public abstract class CharacterData : Node, ICharacterData
{
    private readonly StringBuilder sb = new();

    /// <inheritdoc/>
    [ScriptMember]
    public string Data
    {
        get => sb.ToString();

        set
        {
            sb.Clear();
            sb.Append(value);
        }
    }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public int Length => sb.Length;

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IElement? PreviousElementSibling { get; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IElement? NextElementSibling { get; }

    internal CharacterData(Uri baseURI, string name, string data) : base(baseURI, name, NodeType.Text) => Data = data;

    /// <inheritdoc/>
    [ScriptMember("appendData")]
    public void Append(string data) => sb.Append(data);

    /// <inheritdoc/>
    [ScriptMember("deleteData")]
    public void Remove(int offset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, Length);
        sb.Remove(offset, Math.Min(count, Length - offset));
    }

    /// <inheritdoc/>
    [ScriptMember("insertData")]
    public void Insert(int offset, string data)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, Length);
        sb.Insert(offset, data);
    }

    /// <inheritdoc/>
    [ScriptMember("replaceData")]
    public void Replace(int offset, int count, string data)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, Length);
        sb.Remove(offset, Math.Min(count, Length - offset));
        sb.Insert(offset, data);
    }

    /// <inheritdoc/>
    [ScriptMember("substringData")]
    public string Substring(int offset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(offset, Length);
        return Data.Substring(offset, Math.Min(count, Length - offset));
    }

    /// <inheritdoc/>
    [ScriptMember]
    public void Before(params IEnumerable<INode> nodes) => ChildNode.Before(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void Before(params IEnumerable<string> nodes) => ChildNode.Before(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void After(params IEnumerable<INode> nodes) => ChildNode.After(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void After(params IEnumerable<string> nodes) => ChildNode.After(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void ReplaceWith(params IEnumerable<INode> nodes) => ChildNode.ReplaceWith(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void ReplaceWith(params IEnumerable<string> nodes) => ChildNode.ReplaceWith(nodes);

    /// <inheritdoc/>
    [ScriptMember]
    public void Remove() => ChildNode.Remove();
}