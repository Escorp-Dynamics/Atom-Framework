using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет элемент скрипта.
/// </summary>
public class HTMLScriptElement : IHTMLScriptElement
{
    /// <inheritdoc/>
    [ScriptMember]
    public string InnerText { get; set; }

    /// <inheritdoc/>
    [ScriptMember("textContent")]
    public string? Content { get; set; }

    /// <inheritdoc/>
    [ScriptMember("src")]
    public Uri Source { get; set; }

    /// <inheritdoc/>
    [ScriptMember]
    public string Text { get; set; }

    internal HTMLScriptElement(Uri src, string innerText)
    {
        Source = src;
        InnerText = innerText;
        Text = $"<script>{innerText}</script>";
    }
}