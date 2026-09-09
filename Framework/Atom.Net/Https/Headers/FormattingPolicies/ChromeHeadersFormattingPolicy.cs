namespace Atom.Net.Https.Headers;

/// <summary>
/// Реализация, мимикрирующая Chrome: порядок и casing как у браузера, Cookie crumbling включён.
/// </summary>
public class ChromeHeadersFormattingPolicy : HeadersFormattingPolicy
{
    private static readonly IEnumerable<char> defaultPseudoHeadersOrder = ['m', 'a', 's', 'p'];

    /// <summary>
    /// Живой Chromium 151 на h1 отправляет Sec-Fetch-* в Title-Case (Sec-Fetch-Site), а не
    /// в нижнем регистре; sec-ch-* остаются в нижнем регистре. Приоритет на h1 вообще не шлётся.
    /// </summary>
    protected override bool IsForcedLowercaseH1Core(string nameLower)
    {
        ArgumentNullException.ThrowIfNull(nameLower);

        if (nameLower.Length >= 10 && nameLower.StartsWith("sec-fetch-", StringComparison.Ordinal)) return false;

        return base.IsForcedLowercaseH1Core(nameLower);
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
    /// Fetch и subresources на h1: живой Chromium 151 собирает их В ДРУГОМ порядке, нежели
    /// навигацию — подсказка платформы уходит перед User-Agent, а сам признак агента
    /// оказывается МЕЖДУ подсказками. Priority на h1 не отправляется вовсе.
    /// </summary>
    /// <remarks>
    /// Живой capture (fetch GET/POST, bodyless PUT, img/script/style на loopback):
    /// <c lang="text">Host, Connection, [Content-Length], sec-ch-ua-platform, User-Agent, sec-ch-ua,
    /// [Content-Type], sec-ch-ua-mobile, Accept, [Origin], Sec-Fetch-Site, Sec-Fetch-Mode,
    /// Sec-Fetch-Dest, Referer, Accept-Encoding, Accept-Language</c>. Нестандартные
    /// fetch-заголовки браузер встраивает сразу после User-Agent (capture: X-Probe).
    /// </remarks>
    private static readonly string[] ChromeFetchOrder =
    [
        "host",
        "connection",
        "content-length",
        "sec-ch-ua-platform",
        "user-agent",
        "sec-ch-ua",
        "content-type",
        "sec-ch-ua-mobile",
        "accept",
        "origin",
        "sec-fetch-site",
        "sec-fetch-mode",
        "sec-fetch-dest",
        "referer",
        "accept-encoding",
        "accept-language",
    ];

    /// <summary>
    /// CORS-preflight на h1: живой Chromium 151 отправляет его БЕЗ подсказок клиента вообще,
    /// Accept стоит первым содержательным заголовком, а ACR-заголовки — до Origin и User-Agent.
    /// </summary>
    /// <remarks>
    /// Живой capture (preflight OPTIONS на loopback): <c lang="text">Host, Connection, Accept,
    /// Access-Control-Request-Method, Access-Control-Request-Headers, Origin, User-Agent,
    /// Sec-Fetch-Mode, Sec-Fetch-Site, Sec-Fetch-Dest, Referer, Accept-Encoding,
    /// Accept-Language</c>. Подавление самих подсказок выполняет обработчик запроса:
    /// политика не отличает preflight от fetch, обработчик — отличает.
    /// </remarks>
    private static readonly string[] ChromePreflightOrder =
    [
        "host",
        "connection",
        "accept",
        "access-control-request-method",
        "access-control-request-headers",
        "origin",
        "user-agent",
        "sec-fetch-mode",
        "sec-fetch-site",
        "sec-fetch-dest",
        "referer",
        "accept-encoding",
        "accept-language",
    ];

    /// <inheritdoc/>
    protected override (IEnumerable<string>? Order, string? RemainderAfterKnown) SelectH1Presentation(IDictionary<string, string> input, RequestKind requestKind)
    {
        if (requestKind is not RequestKind.Fetch)
        {
            return (null, null);
        }

        if (TryGetIgnoreCase(input, "access-control-request-method", out _))
        {
            return (ChromePreflightOrder, null);
        }

        return (ChromeFetchOrder, "user-agent");
    }
}