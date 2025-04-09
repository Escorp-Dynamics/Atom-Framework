using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет список токенов DOM.
/// </summary>
public class DOMTokenList : IDOMTokenList
{
    private readonly List<string> tokens = [];

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public int Length => tokens.Count;

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public string this[int index] => tokens[index];

    /// <inheritdoc/>
    [ScriptMember]
    public string Value
    {
        get => string.Join(" ", tokens);

        set
        {
            tokens.Clear();
            if (!string.IsNullOrEmpty(value)) Add(value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
    }

    /// <inheritdoc/>
    [ScriptMember]
    public bool Contains(string token) => tokens.Contains(token);

    /// <inheritdoc/>
    [ScriptMember]
    public void Add([NotNull] params IEnumerable<string> tokens)
    {
        foreach (var token in tokens)
        {
            if (!this.tokens.Contains(token))
                this.tokens.Add(token);
        }
    }

    /// <inheritdoc/>
    [ScriptMember]
    public void Remove([NotNull] params IEnumerable<string> tokens)
    {
        foreach (var token in tokens) this.tokens.Remove(token);
    }

    /// <inheritdoc/>
    [ScriptMember]
    public bool Toggle(string token, bool force)
    {
        if (tokens.Contains(token))
        {
            if (!force)
            {
                tokens.Remove(token);
                return default;
            }
        }
        else
        {
            if (!force)
            {
                tokens.Add(token);
                return true;
            }
        }

        return tokens.Contains(token);
    }

    /// <inheritdoc/>
    [ScriptMember]
    public bool Toggle(string token) => Toggle(token, default);

    /// <inheritdoc/>
    [ScriptMember]
    public bool Replace(string token, string newToken)
    {
        if (tokens.Remove(token))
        {
            tokens.Add(newToken);
            return true;
        }

        return default;
    }

    /// <inheritdoc/>
    [ScriptMember]
    public bool Supports(string token) => throw new NotImplementedException();

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.None)]
    public IEnumerator<string> GetEnumerator() => tokens.GetEnumerator();

    [ScriptMember(ScriptAccess.None)]
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}