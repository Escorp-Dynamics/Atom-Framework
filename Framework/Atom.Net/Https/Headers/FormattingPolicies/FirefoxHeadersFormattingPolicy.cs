namespace Atom.Net.Https.Headers;

/// <summary>
/// Правила оформления заголовков, свойственные Firefox.
/// </summary>
/// <remarks>
/// Отличается от движков Chromium и порядком псевдозаголовков, и порядком обычных. Оба сняты с
/// настоящего Firefox 154: первый — по сигнатуре HTTP/2 на зеркале
/// (<c>1:65536;2:0;4:131072;5:16384|12517377|0|m,p,a,s</c>), второй — из собственного журнала
/// браузера (<c>MOZ_LOG=nsHttp:5</c>), где список заголовков виден до кодирования.
/// </remarks>
public class FirefoxHeadersFormattingPolicy : HeadersFormattingPolicy
{
    private static readonly IEnumerable<char> defaultPseudoHeadersOrder = ['m', 'p', 'a', 's'];

    /// <summary>
    /// Порядок обычных заголовков, снятый с Firefox 154.
    /// </summary>
    /// <remarks>
    /// Замер (запрос навигации с cookie и referer, лишние для HTTP/2 <c>Host</c> и
    /// <c>Connection</c> опущены):
    ///
    /// <code>
    /// User-Agent  Accept  Accept-Language  Accept-Encoding  Referer  Cookie
    /// Upgrade-Insecure-Requests  Sec-Fetch-Dest  Sec-Fetch-Mode  Sec-Fetch-Site  Sec-Fetch-User
    /// Priority
    /// </code>
    ///
    /// Три отличия от Chromium существенны: язык идёт РАНЬШЕ кодировок, признак навигации стоит
    /// не в начале, а после них, и порядок <c>sec-fetch-*</c> обратный хромиумовскому.
    ///
    /// Подсказок клиента (<c>sec-ch-ua*</c>) Firefox не отправляет вовсе, поэтому их в списке нет.
    /// </remarks>
    private static readonly string[] FirefoxOrderCommon =
    [
        "host",
        "user-agent",
        "accept",
        "accept-language",
        "accept-encoding",
        "connection",
        "upgrade-insecure-requests",
        "referer",
        "origin",
        "cookie",
        "sec-fetch-dest",
        "sec-fetch-mode",
        "sec-fetch-site",
        "sec-fetch-user",
        "priority",
        "dnt",
        "pragma",
        "cache-control",
        "te",
    ];

    /// <inheritdoc/>
    protected override IEnumerable<string> OrderCommon => FirefoxOrderCommon;

    /// <inheritdoc/>
    public override IReadOnlyDictionary<RequestKind, IEnumerable<char>> PseudoHeadersOrder { get; set; } = new Dictionary<RequestKind, IEnumerable<char>>
    {
        { RequestKind.Navigation, defaultPseudoHeadersOrder },
        { RequestKind.Preload, defaultPseudoHeadersOrder },
        { RequestKind.ModulePreload, defaultPseudoHeadersOrder },
        { RequestKind.Prefetch, defaultPseudoHeadersOrder },
        { RequestKind.Fetch, defaultPseudoHeadersOrder },
        { RequestKind.ServiceWorker, defaultPseudoHeadersOrder },
        { RequestKind.Unknown, defaultPseudoHeadersOrder },
    };

    /// <summary>
    /// Живой Firefox 154 на h1 отправляет ВСЕ заголовки в Title-Case, включая Sec-Fetch-*
    /// и Priority (capture: навигация на loopback) — принудительный lowercase не применяется.
    /// </summary>
    protected override bool IsForcedLowercaseH1Core(string nameLower)
    {
        ArgumentNullException.ThrowIfNull(nameLower);

        return false;
    }
}