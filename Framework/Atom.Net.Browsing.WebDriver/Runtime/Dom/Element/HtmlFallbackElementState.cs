using System.Net;

namespace Atom.Net.Browsing.WebDriver;

internal sealed class HtmlFallbackElementState
{
    private readonly IReadOnlyDictionary<string, string> attributes;

    private HtmlFallbackElementState(string tagName, string innerHtml, string innerText, IReadOnlyDictionary<string, string> attributes, string path)
    {
        TagName = tagName;
        InnerHtml = innerHtml;
        InnerText = innerText;
        this.attributes = attributes;
        Path = path;
    }

    internal string TagName { get; }

    internal string InnerHtml { get; }

    internal string InnerText { get; }

    internal string Path { get; }

    internal IEnumerable<string> ClassList
        => TryGetAttribute("class")?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    internal static HtmlFallbackElementState? Create(string? markup)
    {
        if (string.IsNullOrWhiteSpace(markup))
            return null;

        var tagStart = FindTagStart(markup);
        if (tagStart < 0)
            return null;

        var tagEnd = markup.IndexOf('>', tagStart + 1);
        if (tagEnd < 0)
            return null;

        var tagContent = markup[(tagStart + 1)..tagEnd].Trim();
        var selfClosing = tagContent.EndsWith('/');
        if (selfClosing)
            tagContent = tagContent[..^1].TrimEnd();

        var separatorIndex = tagContent.IndexOfAny([' ', '\t', '\r', '\n']);
        var tagName = (separatorIndex >= 0 ? tagContent[..separatorIndex] : tagContent).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(tagName))
            return null;

        var attributesSegment = separatorIndex >= 0 ? tagContent[(separatorIndex + 1)..] : string.Empty;
        var attributes = ParseAttributes(attributesSegment);
        var innerHtml = selfClosing ? string.Empty : ExtractInnerHtml(markup, tagEnd + 1, tagName);
        var innerText = StripTags(innerHtml);

        return new HtmlFallbackElementState(tagName, innerHtml, innerText, attributes, string.Concat('/', tagName, "[1]"));
    }

    internal static HtmlFallbackElementState CreateResolved(string tagName, string innerHtml, string innerText, IReadOnlyDictionary<string, string> attributes, string path)
        => new(tagName, innerHtml, innerText, attributes, path);

    internal string? TryGetAttribute(string attributeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        return attributes.TryGetValue(attributeName, out var value) ? value : null;
    }

    internal bool HasBooleanAttribute(string attributeName)
        => attributes.ContainsKey(attributeName);

    internal bool GetBooleanAttributeValue(string attributeName)
    {
        if (!attributes.TryGetValue(attributeName, out var value))
            return false;

        return !value.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    private static int FindTagStart(string markup)
    {
        for (var index = 0; index < markup.Length - 1; index++)
        {
            if (markup[index] == '<' && char.IsLetter(markup[index + 1]))
                return index;
        }

        return -1;
    }

    private static Dictionary<string, string> ParseAttributes(string value)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        while (index < value.Length)
        {
            index = SkipWhitespace(value, index);
            if (index >= value.Length)
                break;

            var name = ReadName(value, ref index);
            if (string.IsNullOrWhiteSpace(name))
                break;

            index = SkipWhitespace(value, index);
            attributes[name] = ReadValue(value, name, ref index);
        }

        return attributes;
    }

    private static int SkipWhitespace(string value, int index)
    {
        while (index < value.Length && char.IsWhiteSpace(value[index]))
            index++;

        return index;
    }

    private static string ReadName(string value, ref int index)
    {
        var nameStart = index;
        while (index < value.Length && !char.IsWhiteSpace(value[index]) && value[index] != '=')
            index++;

        return value[nameStart..index].Trim();
    }

    private static string ReadValue(string value, string name, ref int index)
    {
        if (index >= value.Length || value[index] != '=')
            return name;

        index = SkipWhitespace(value, index + 1);
        if (index >= value.Length)
            return string.Empty;

        if (value[index] is '"' or '\'')
            return ReadQuotedValue(value, ref index);

        return ReadUnquotedValue(value, ref index);
    }

    private static string ReadQuotedValue(string value, ref int index)
    {
        var quote = value[index++];
        var valueStart = index;
        while (index < value.Length && value[index] != quote)
            index++;

        var result = value[valueStart..Math.Min(index, value.Length)];
        if (index < value.Length)
            index++;

        return result;
    }

    private static string ReadUnquotedValue(string value, ref int index)
    {
        var valueStart = index;
        while (index < value.Length && !char.IsWhiteSpace(value[index]))
            index++;

        return value[valueStart..index];
    }

    private static string ExtractInnerHtml(string markup, int contentStart, string tagName)
    {
        var closingTag = string.Concat("</", tagName, ">");
        var closingTagStart = markup.LastIndexOf(closingTag, StringComparison.OrdinalIgnoreCase);
        if (closingTagStart < contentStart)
            return string.Empty;

        return markup[contentStart..closingTagStart];
    }

    private static string StripTags(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        var buffer = new char[html.Length];
        var count = 0;
        var insideTag = false;

        foreach (var character in html)
        {
            if (character == '<')
            {
                insideTag = true;
                continue;
            }

            if (character == '>')
            {
                insideTag = false;
                continue;
            }

            if (!insideTag)
                buffer[count++] = character;
        }

        return WebUtility.HtmlDecode(new string(buffer, 0, count)).Trim();
    }
}