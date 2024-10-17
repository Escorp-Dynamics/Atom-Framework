using System.Diagnostics.CodeAnalysis;
using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет доверенный скрипт в формате ссылки.
/// </summary>
public class TrustedScriptURL : ITrustedScriptURL
{
    private readonly Uri url;

    internal TrustedScriptURL(Uri url) => this.url = url;

    internal TrustedScriptURL(string url) : this(new Uri(url)) { }

    internal TrustedScriptURL() : this(string.Empty) { }

    /// <inheritdoc/>
    [ScriptMember]
    public override string ToString() => url.AbsoluteUri;

    /// <inheritdoc/>
    [ScriptMember]
    public string ToJSON() => $"\"{url.AbsoluteUri}\"";

    /// <summary>
    /// Преобразует адрес в скрипт.
    /// </summary>
    /// <param name="url">Адрес скрипта.</param>
    [ScriptMember(ScriptAccess.None)]
    public static TrustedScriptURL FromUri(Uri url) => new(url);

    /// <summary>
    /// Преобразует скрипт в адрес.
    /// </summary>
    /// <param name="script">Скрипт.</param>
    [ScriptMember(ScriptAccess.None)]
    public static Uri ToUri([NotNull] TrustedScriptURL script) => script.url;

    /// <summary>
    /// Преобразует адрес в скрипт.
    /// </summary>
    /// <param name="url">Адрес скрипта.</param>
    [ScriptMember]
    public static explicit operator TrustedScriptURL(Uri url) => FromUri(url);

    /// <summary>
    /// Преобразует скрипт в адрес.
    /// </summary>
    /// <param name="script">Скрипт.</param>
    [ScriptMember]
    public static implicit operator Uri(TrustedScriptURL script) => ToUri(script);
}