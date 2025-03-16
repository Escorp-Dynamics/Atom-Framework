using System.Diagnostics.CodeAnalysis;
using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет доверенный скрипт.
/// </summary>
public class TrustedScript : ITrustedScript
{
    private readonly string content;

    internal TrustedScript(string content) => this.content = content;

    internal TrustedScript() : this(string.Empty) { }

    /// <inheritdoc/>
    [ScriptMember]
    public override string ToString() => content;

    /// <inheritdoc/>
    [ScriptMember]
    public string ToJSON() => $"\"{content}\"";

    /// <summary>
    /// Преобразует строку в скрипт.
    /// </summary>
    /// <param name="content">Содержимое скрипта.</param>
    [ScriptMember(ScriptAccess.None)]
    public static TrustedScript FromString(string content) => new(content);

    /// <summary>
    /// Преобразует строку в скрипт.
    /// </summary>
    /// <param name="content">Содержимое скрипта.</param>
    [ScriptMember]
    public static explicit operator TrustedScript(string content) => FromString(content);

    /// <summary>
    /// Преобразует скрипт в строку.
    /// </summary>
    /// <param name="script">Скрипт.</param>
    [ScriptMember]
    public static implicit operator string([NotNull] TrustedScript script) => script.content;
}