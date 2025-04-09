using Atom.Web.Browsing.DOM;

namespace Atom.Web.Browsing;

/// <summary>
/// Представляет парсер HTML в DOM.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="DOMParser"/>
/// </remarks>
/// <param name="url">Ссылка страницы.</param>
/// <param name="html">Исходный HTML.</param>
public readonly ref struct DOMParser(Uri url, ReadOnlySpan<char> html)
{
    [Flags]
    private enum TokenType
    {
        Unknown = 0,
        StartTag = 1,
        EndTag = 2,
        SpecialTag = 4,
        Comment = 8,
        DocType = 16,
    }

    private readonly Uri url = url;

    private readonly ReadOnlySpan<char> html = html;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DOMParser"/>.
    /// </summary>
    /// <param name="html">Исходный HTML.</param>
    public DOMParser(ReadOnlySpan<char> html) : this(new Uri("about:blank"), html) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DOMParser"/>.
    /// </summary>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="html">Исходный HTML.</param>
    public DOMParser(Uri url, string html) : this(url, html.AsSpan()) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DOMParser"/>.
    /// </summary>
    /// <param name="html">Исходный HTML.</param>
    public DOMParser(string html) : this(html.AsSpan()) { }

    /// <summary>
    /// Парсит HTML в DOM.
    /// </summary>
    public IDocument Parse()
    {
        var document = new Document();

        var tokenType = TokenType.Unknown;
        var tokenStartIndex = 0;

        for (var i = 0; i < html.Length; ++i)
        {
            var c = html[i];

            // Начало тега.
            if (c is '<')
            {
                tokenType = TokenType.StartTag;
                continue;
            }

            // Специализированный тег.
            if (tokenType.HasFlag(TokenType.StartTag) && c is '!')
            {
                tokenStartIndex = i;
                tokenType |= TokenType.SpecialTag;
                continue;
            }

            // Комментарий.
            if (tokenType.HasFlag(TokenType.SpecialTag) && html[tokenStartIndex..i] is "--")
            {
                tokenStartIndex = i;
                tokenType &= TokenType.SpecialTag;
                tokenType |= TokenType.Comment;
                continue;
            }

            if (tokenType.HasFlag(TokenType.Comment))
            {
                var tokenEndIndex = i - 3;
                if (c is not '>' || html[tokenEndIndex..(i - 1)] is not "--") continue;

                _ = new Comment(new string(html[tokenStartIndex..tokenEndIndex].Trim()));
                continue;
            }

            // doctype.
            if (tokenType.HasFlag(TokenType.SpecialTag))
            {
                if (c is ' ' && !tokenType.HasFlag(TokenType.DocType))
                {
                    var type = html[tokenStartIndex..(i - 1)];
                    if (type is not "DOCTYPE") throw new DOMParserException($"Неизвестный тип специализированного токена: {type}");

                    tokenStartIndex = i;
                    tokenType |= TokenType.DocType;
                }

                if (c is '>')
                {
                    if (!tokenType.HasFlag(TokenType.DocType)) throw new DOMParserException("Ошибка парсинга doctype");
                    if (url.AbsoluteUri is not "about:blank") document.DocType = new DocumentType(url, new string(html[tokenStartIndex..(i - 1)]), string.Empty, string.Empty);
                    continue;
                }
            }

            // TODO
        }

        return document;
    }

    /// <summary>
    /// Парсит HTML в DOM.
    /// </summary>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="html">Исходный HTML.</param>
    public static IDocument Parse(Uri url, ReadOnlySpan<char> html)
    {
        var parser = new DOMParser(url, html);
        return parser.Parse();
    }

    /// <summary>
    /// Парсит HTML в DOM.
    /// </summary>
    /// <param name="html">Исходный HTML.</param>
    public static IDocument Parse(ReadOnlySpan<char> html) => Parse(new Uri("about:blank"), html);

    /// <summary>
    /// Парсит HTML в DOM.
    /// </summary>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="html">Исходный HTML.</param>
    public static IDocument Parse(Uri url, string html) => Parse(url, html.AsSpan());

    /// <summary>
    /// Парсит HTML в DOM.
    /// </summary>
    /// <param name="html">Исходный HTML.</param>
    public static IDocument Parse(string html) => Parse(html.AsSpan());
}