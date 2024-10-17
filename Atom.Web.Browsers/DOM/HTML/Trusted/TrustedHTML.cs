using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет доверенный HTML.
/// </summary>
public class TrustedHTML : ITrustedHTML
{
    private readonly string content;

    internal TrustedHTML(string content) => this.content = content;

    internal TrustedHTML() : this(string.Empty) { }

    /// <inheritdoc/>
    [ScriptMember]
    public override string ToString() => content;

    /// <inheritdoc/>
    [ScriptMember]
    public string ToJSON() => $"\"{content}\"";
}