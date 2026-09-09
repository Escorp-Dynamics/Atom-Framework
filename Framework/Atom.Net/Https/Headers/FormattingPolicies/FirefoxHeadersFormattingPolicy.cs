namespace Atom.Net.Https.Headers;

/// <summary>
/// Правила оформления заголовков, свойственные Firefox.
/// </summary>
/// <remarks>
/// Отличается от движков Chromium и порядком псевдозаголовков, и порядком обычных. Оба сняты с
/// настоящего Firefox 154: первый — по сигнатуре HTTP/2 на зеркале
/// (<c lang="text">1:65536;2:0;4:131072;5:16384|12517377|0|m,p,a,s</c>), второй — из собственного журнала
/// браузера (<c lang="text">MOZ_LOG=nsHttp:5</c>), где список заголовков виден до кодирования.
/// </remarks>
public class FirefoxHeadersFormattingPolicy : HeadersFormattingPolicy
{
    private static readonly IEnumerable<char> defaultPseudoHeadersOrder = ['m', 'p', 'a', 's'];

    /// <summary>
    /// Порядок обычных заголовков, снятый с Firefox 154.
    /// </summary>
    /// <remarks>
    /// Замер (запрос навигации с cookie и referer, лишние для HTTP/2 <c lang="text">Host</c> и
    /// <c lang="text">Connection</c> опущены):
    ///
    /// <code lang="csharp">
    /// User-Agent  Accept  Accept-Language  Accept-Encoding  Referer  Cookie
    /// Upgrade-Insecure-Requests  Sec-Fetch-Dest  Sec-Fetch-Mode  Sec-Fetch-Site  Sec-Fetch-User
    /// Priority
    /// </code>
    ///
    /// Три отличия от Chromium существенны: язык идёт РАНЬШЕ кодировок, признак навигации стоит
    /// не в начале, а после них, и порядок <c lang="text">sec-fetch-*</c> обратный хромиумовскому.
    ///
    /// Живой capture h1 (Chromium/Firefox 154 на loopback) уточняет позицию <c lang="text">Referer</c>:
    /// он стоит сразу после <c lang="text">Connection</c> (iframe-навигация), а на h2, где соединный
    /// заголовок не пишется, журнал показывает его сразу после кодировок — одно и то же место.
    ///
    /// Подсказок клиента (<c lang="text">sec-ch-ua*</c>) Firefox не отправляет вовсе, поэтому их в списке нет.
    /// </remarks>
    private static readonly string[] FirefoxOrderCommon =
    [
        "host",
        "user-agent",
        "accept",
        "accept-language",
        "accept-encoding",
        "connection",
        "referer",
        "origin",
        "cookie",
        "upgrade-insecure-requests",
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

    /// <summary>
    /// CORS-mode fetch: реферер уходит до соединного заголовка, тело запроса — между ним и Origin.
    /// </summary>
    /// <remarks>
    /// Живой capture Firefox 154 (fetch POST text/plain на loopback):
    /// <c lang="text">... Accept-Encoding, Referer, Content-Type, Content-Length, Origin, Connection, ...</c>
    /// </remarks>
    private static readonly string[] FirefoxFetchCorsOrder =
    [
        "host",
        "user-agent",
        "accept",
        "accept-language",
        "accept-encoding",
        "referer",
        "content-type",
        "content-length",
        "origin",
        "connection",
        "sec-fetch-dest",
        "sec-fetch-mode",
        "sec-fetch-site",
        "priority",
    ];

    /// <summary>
    /// CORS-mode fetch без тела: Content-Length: 0 Firefox дописывает В САМЫЙ КОНЕЦ, после
    /// Priority (живой capture: bodyless PUT). Content-Type при этом отсутствует.
    /// </summary>
    private static readonly string[] FirefoxFetchCorsWithoutBodyOrder =
    [
        "host",
        "user-agent",
        "accept",
        "accept-language",
        "accept-encoding",
        "referer",
        "origin",
        "connection",
        "sec-fetch-dest",
        "sec-fetch-mode",
        "sec-fetch-site",
        "priority",
        "content-length",
    ];

    /// <summary>
    /// CORS-preflight: ACR-заголовки уходят сразу после кодировок, до реферера и Origin.
    /// </summary>
    /// <remarks>
    /// Живой capture Firefox 154 (preflight OPTIONS): <c lang="text">... Accept-Encoding,
    /// Access-Control-Request-Method, Access-Control-Request-Headers, Referer, Origin,
    /// Connection, Sec-Fetch-Dest, ...</c>
    /// </remarks>
    private static readonly string[] FirefoxPreflightOrder =
    [
        "host",
        "user-agent",
        "accept",
        "accept-language",
        "accept-encoding",
        "access-control-request-method",
        "access-control-request-headers",
        "referer",
        "origin",
        "connection",
        "sec-fetch-dest",
        "sec-fetch-mode",
        "sec-fetch-site",
        "priority",
    ];

    /// <inheritdoc/>
    protected override IEnumerable<string> OrderCommon => FirefoxOrderCommon;

    /// <inheritdoc/>
    protected override (IEnumerable<string>? Order, string? RemainderAfterKnown) SelectH1Presentation(IDictionary<string, string> input, RequestKind requestKind)
    {
        if (requestKind is not RequestKind.Fetch)
        {
            return (null, null);
        }

        if (TryGetIgnoreCase(input, "access-control-request-method", out _))
        {
            return (FirefoxPreflightOrder, null);
        }

        // no-cors subresources (img/script/style) ходят общим навигационным порядком —
        // живой capture совпадает с ним построчно.
        if (!TryGetIgnoreCase(input, "sec-fetch-mode", out var mode)
            || !string.Equals(mode, "cors", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        // Нестандартные fetch-заголовки живой Firefox встраивает сразу после Referer
        // (capture: X-Probe между Referer и Origin).
        return TryGetIgnoreCase(input, "content-type", out _)
            ? (FirefoxFetchCorsOrder, "referer")
            : (FirefoxFetchCorsWithoutBodyOrder, "referer");
    }

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