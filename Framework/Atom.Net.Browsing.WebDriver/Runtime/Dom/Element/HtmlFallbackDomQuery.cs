using System.Globalization;
using System.Net;

namespace Atom.Net.Browsing.WebDriver;

internal static class HtmlFallbackDomQuery
{
    internal static HtmlFallbackElementState? FindFirst(string? markup, ElementSelector selector)
    {
        var matches = FindAll(markup, selector);
        return matches.Count == 0 ? null : matches[0];
    }

    internal static HtmlFallbackElementState? FindByPath(string? markup, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var matches = Enumerate(markup, state => string.Equals(state.Path, path, StringComparison.Ordinal));
        return matches.Count == 0 ? null : matches[0];
    }

    internal static IReadOnlyList<HtmlFallbackElementState> FindChildren(string? markup, string parentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentPath);

        var parentDepth = CountPathSegments(parentPath);
        var prefix = parentPath + "/";
        return Enumerate(markup, state => state.Path.StartsWith(prefix, StringComparison.Ordinal)
            && CountPathSegments(state.Path) == parentDepth + 1);
    }

    internal static IReadOnlyList<HtmlFallbackElementState> FindSiblings(string? markup, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var parentPath = GetParentPath(path);
        if (string.IsNullOrWhiteSpace(parentPath))
            return [];

        var depth = CountPathSegments(path);
        var prefix = parentPath + "/";
        return Enumerate(markup, state => !string.Equals(state.Path, path, StringComparison.Ordinal)
            && state.Path.StartsWith(prefix, StringComparison.Ordinal)
            && CountPathSegments(state.Path) == depth);
    }

    internal static IReadOnlyList<HtmlFallbackElementState> FindAll(string? markup, ElementSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        return Enumerate(markup, state => Matches(state, selector));
    }

    private static List<HtmlFallbackElementState> Enumerate(string? markup, Func<HtmlFallbackElementState, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        if (string.IsNullOrWhiteSpace(markup))
            return [];

        List<HtmlFallbackElementState> matches = [];
        Stack<OpenElement> stack = [];
        var siblingCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        while (index < markup.Length)
            index = ProcessTag(markup, predicate, stack, siblingCounters, matches, index);

        AddUnclosedMatches(predicate, stack, matches);

        return matches;
    }

    private static int ProcessTag(
        string markup,
        Func<HtmlFallbackElementState, bool> predicate,
        Stack<OpenElement> stack,
        Dictionary<string, int> siblingCounters,
        List<HtmlFallbackElementState> matches,
        int index)
    {
        if (markup[index] != '<')
            return index + 1;

        if (IsComment(markup, index))
            return SkipComment(markup, index);

        if (IsClosingTag(markup, index, out var closingTagName, out var closingEnd))
            return CloseTag(markup, predicate, stack, closingTagName, closingEnd, matches);

        if (!TryReadOpeningTag(markup, index, out var openingTag, out var tagEnd, out var selfClosing)
            || string.IsNullOrWhiteSpace(openingTag.TagName))
        {
            return index + 1;
        }

        return OpenTag(predicate, stack, siblingCounters, matches, openingTag, tagEnd, selfClosing);
    }

    private static int CloseTag(
        string markup,
        Func<HtmlFallbackElementState, bool> predicate,
        Stack<OpenElement> stack,
        string closingTagName,
        int closingEnd,
        List<HtmlFallbackElementState> matches)
    {
        CloseElementsUntil(markup, predicate, stack, closingTagName, closingEnd, matches);
        return closingEnd + 1;
    }

    private static int OpenTag(
        Func<HtmlFallbackElementState, bool> predicate,
        Stack<OpenElement> stack,
        Dictionary<string, int> siblingCounters,
        List<HtmlFallbackElementState> matches,
        OpeningTag openingTag,
        int tagEnd,
        bool selfClosing)
    {
        var path = CreatePath(stack, siblingCounters, openingTag.TagName);
        if (selfClosing || IsVoidElement(openingTag.TagName))
        {
            AddSelfClosingMatch(predicate, matches, openingTag, path);
            return tagEnd + 1;
        }

        stack.Push(new OpenElement(openingTag.TagName, openingTag.Attributes, path, tagEnd + 1));
        return tagEnd + 1;
    }

    private static string CreatePath(Stack<OpenElement> stack, Dictionary<string, int> siblingCounters, string tagName)
    {
        var parentPath = stack.Count > 0 ? stack.Peek().Path : string.Empty;
        var counterKey = string.Concat(parentPath, "/", tagName);
        siblingCounters.TryGetValue(counterKey, out var elementIndex);
        elementIndex++;
        siblingCounters[counterKey] = elementIndex;
        return string.Concat(parentPath, "/", tagName, "[", elementIndex.ToString(CultureInfo.InvariantCulture), "]");
    }

    private static void AddSelfClosingMatch(Func<HtmlFallbackElementState, bool> predicate, List<HtmlFallbackElementState> matches, OpeningTag openingTag, string path)
    {
        var snapshot = HtmlFallbackElementState.CreateResolved(
            openingTag.TagName,
            string.Empty,
            string.Empty,
            openingTag.Attributes,
            path);
        if (predicate(snapshot))
            matches.Add(snapshot);
    }

    private static void AddUnclosedMatches(Func<HtmlFallbackElementState, bool> predicate, Stack<OpenElement> stack, List<HtmlFallbackElementState> matches)
    {
        while (stack.Count > 0)
        {
            var openElement = stack.Pop();
            var snapshot = HtmlFallbackElementState.CreateResolved(openElement.TagName, string.Empty, string.Empty, openElement.Attributes, openElement.Path);
            if (predicate(snapshot))
                matches.Add(snapshot);
        }
    }

    private static void CloseElementsUntil(
        string markup,
        Func<HtmlFallbackElementState, bool> predicate,
        Stack<OpenElement> stack,
        string closingTagName,
        int closingStart,
        List<HtmlFallbackElementState> matches)
    {
        while (stack.Count > 0)
        {
            var openElement = stack.Pop();
            var innerHtml = closingStart > openElement.ContentStart && closingStart <= markup.Length
                ? markup[openElement.ContentStart..closingStart]
                : string.Empty;
            var snapshot = HtmlFallbackElementState.CreateResolved(
                openElement.TagName,
                innerHtml,
                StripTags(innerHtml),
                openElement.Attributes,
                openElement.Path);
            if (predicate(snapshot))
                matches.Add(snapshot);

            if (string.Equals(openElement.TagName, closingTagName, StringComparison.OrdinalIgnoreCase))
                return;
        }
    }

    private static bool Matches(HtmlFallbackElementState state, ElementSelector selector)
        => selector.Strategy switch
        {
            ElementSelectorStrategy.Css => MatchesCss(state, selector.Value),
            ElementSelectorStrategy.Id => string.Equals(state.TryGetAttribute("id"), selector.Value, StringComparison.Ordinal),
            ElementSelectorStrategy.Name => string.Equals(state.TryGetAttribute("name"), selector.Value, StringComparison.Ordinal),
            ElementSelectorStrategy.TagName => string.Equals(state.TagName, selector.Value, StringComparison.OrdinalIgnoreCase),
            ElementSelectorStrategy.Text => state.InnerText.Contains(selector.Value, StringComparison.Ordinal),
            _ => false,
        };

    private static bool MatchesCss(HtmlFallbackElementState state, string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return false;

        selector = selector.Trim();
        if (ContainsUnsupportedCssSyntax(selector))
            return false;

        var selectorParts = ParseCssSelector(selector);
        if (selectorParts is null)
            return false;

        if (!string.IsNullOrWhiteSpace(selectorParts.TagName) && !string.Equals(state.TagName, selectorParts.TagName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (selectorParts.Id is not null && !string.Equals(state.TryGetAttribute("id"), selectorParts.Id, StringComparison.Ordinal))
            return false;

        if (selectorParts.Classes.Count == 0)
            return true;

        var classList = state.ClassList.ToHashSet(StringComparer.Ordinal);
        return selectorParts.Classes.TrueForAll(classList.Contains);
    }

    private static bool ContainsUnsupportedCssSyntax(string selector)
        => selector.Contains(' ') || selector.Contains('>') || selector.Contains('+') || selector.Contains('~') || selector.Contains('[') || selector.Contains(':');

    private static CssSelectorParts? ParseCssSelector(string selector)
    {
        string? tagName = null;
        string? id = null;
        List<string> classes = [];
        var remainder = selector;

        var firstSpecialIndex = NextSpecialIndex(remainder);
        if (firstSpecialIndex > 0)
        {
            tagName = remainder[..firstSpecialIndex];
            remainder = remainder[firstSpecialIndex..];
        }
        else if (firstSpecialIndex < 0)
        {
            return new CssSelectorParts(TagName: remainder, Id: null, Classes: classes);
        }

        while (!string.IsNullOrEmpty(remainder))
        {
            if (!TryReadCssSegment(ref remainder, idMarker: '#', out var idSegment, out var classSegment))
                return null;

            if (idSegment is not null)
            {
                id = idSegment;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(classSegment))
                classes.Add(classSegment!);
        }

        return new CssSelectorParts(TagName: tagName, Id: id, Classes: classes);
    }

    private static bool TryReadCssSegment(ref string remainder, char idMarker, out string? idSegment, out string? classSegment)
    {
        idSegment = null;
        classSegment = null;

        if (remainder[0] == idMarker)
        {
            remainder = remainder[1..];
            var nextIndex = NextSpecialIndex(remainder);
            idSegment = nextIndex >= 0 ? remainder[..nextIndex] : remainder;
            remainder = nextIndex >= 0 ? remainder[nextIndex..] : string.Empty;
            return true;
        }

        if (remainder[0] != '.')
            return false;

        remainder = remainder[1..];
        var nextClassIndex = NextSpecialIndex(remainder);
        classSegment = nextClassIndex >= 0 ? remainder[..nextClassIndex] : remainder;
        remainder = nextClassIndex >= 0 ? remainder[nextClassIndex..] : string.Empty;
        return true;
    }

    private static int NextSpecialIndex(string value)
    {
        var hashIndex = value.IndexOf('#');
        var dotIndex = value.IndexOf('.');

        return hashIndex >= 0 && dotIndex >= 0 ? Math.Min(hashIndex, dotIndex) : Math.Max(hashIndex, dotIndex);
    }

    private static bool TryReadOpeningTag(string markup, int startIndex, out OpeningTag openingTag, out int tagEnd, out bool selfClosing)
    {
        openingTag = default;
        tagEnd = startIndex;
        selfClosing = false;

        if (startIndex + 1 >= markup.Length || !char.IsLetter(markup[startIndex + 1]))
            return false;

        tagEnd = markup.IndexOf('>', startIndex + 1);
        if (tagEnd < 0)
            return false;

        var tagContent = markup[(startIndex + 1)..tagEnd].Trim();
        selfClosing = tagContent.EndsWith('/');
        if (selfClosing)
            tagContent = tagContent[..^1].TrimEnd();

        var separatorIndex = tagContent.IndexOfAny([' ', '\t', '\r', '\n']);
        var tagName = (separatorIndex >= 0 ? tagContent[..separatorIndex] : tagContent).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(tagName))
            return false;

        var attributesSegment = separatorIndex >= 0 ? tagContent[(separatorIndex + 1)..] : string.Empty;
        openingTag = new OpeningTag(tagName, ParseAttributes(attributesSegment));
        return true;
    }

    private static bool IsComment(string markup, int index)
        => index + 3 < markup.Length
           && markup[index] == '<'
           && markup[index + 1] == '!'
           && markup[index + 2] == '-'
           && markup[index + 3] == '-';

    private static int SkipComment(string markup, int index)
    {
        var end = markup.IndexOf("-->", index, StringComparison.Ordinal);
        return end >= 0 ? end + 2 : markup.Length - 1;
    }

    private static bool IsClosingTag(string markup, int index, out string tagName, out int closingEnd)
    {
        tagName = string.Empty;
        closingEnd = index;

        if (index + 2 >= markup.Length || markup[index] != '<' || markup[index + 1] != '/')
            return false;

        closingEnd = markup.IndexOf('>', index + 2);
        if (closingEnd < 0)
            return false;

        tagName = markup[(index + 2)..closingEnd].Trim().ToLowerInvariant();
        return !string.IsNullOrWhiteSpace(tagName);
    }

    private static bool IsVoidElement(string tagName)
        => tagName is "area" or "base" or "br" or "col" or "embed" or "hr" or "img" or "input" or "link" or "meta" or "param" or "source" or "track" or "wbr";

    private static string? GetParentPath(string path)
    {
        var lastSeparatorIndex = path.LastIndexOf('/');
        return lastSeparatorIndex <= 0 ? null : path[..lastSeparatorIndex];
    }

    private static int CountPathSegments(string path)
        => path.Count(static character => character == '/');

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
        var start = index;
        while (index < value.Length && !char.IsWhiteSpace(value[index]) && value[index] != '=')
            index++;

        return value[start..index].Trim();
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
        var start = index;
        while (index < value.Length && value[index] != quote)
            index++;

        var result = value[start..Math.Min(index, value.Length)];
        if (index < value.Length)
            index++;

        return result;
    }

    private static string ReadUnquotedValue(string value, ref int index)
    {
        var start = index;
        while (index < value.Length && !char.IsWhiteSpace(value[index]))
            index++;

        return value[start..index];
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

    private readonly record struct OpeningTag(string TagName, IReadOnlyDictionary<string, string> Attributes);

    private readonly record struct OpenElement(string TagName, IReadOnlyDictionary<string, string> Attributes, string Path, int ContentStart);

    private sealed record CssSelectorParts(string? TagName, string? Id, List<string> Classes);
}