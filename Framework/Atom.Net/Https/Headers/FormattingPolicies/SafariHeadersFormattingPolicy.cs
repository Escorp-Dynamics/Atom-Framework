namespace Atom.Net.Https.Headers;

/// <summary>
/// Правила оформления заголовков, свойственные Safari.
/// </summary>
/// <remarks>
/// Отличается от движков Chromium и Gecko и порядком псевдозаголовков (<c>m,s,a,p</c>), и
/// порядком обычных, и самим их составом.
///
/// Порядок снят с записанного обмена Safari 18.4 на macOS — файл эталонных подписей проекта
/// <c>curl-impersonate</c> (<c>tests/signatures/safari_18.4_macOS.yaml</c>). Восемь заголовков:
///
/// <code>
/// sec-fetch-dest  user-agent  accept  sec-fetch-site  sec-fetch-mode
/// accept-language  priority  accept-encoding
/// </code>
///
/// Три особенности видны сразу. Признака навигации (<c>upgrade-insecure-requests</c>) Safari не
/// отправляет вовсе. Подсказок клиента (<c>sec-ch-ua*</c>) — тоже, их поддерживают только движки
/// Chromium. И <c>sec-fetch-user</c> у Safari не реализован, что подтверждается и таблицами
/// совместимости.
///
/// Самое приметное — <c>accept-encoding</c> стоит ПОСЛЕДНИМ, тогда как у Chromium он в середине,
/// а у Firefox сразу после языка. Одного этого достаточно, чтобы отличить три браузера.
///
/// Положение <c>referer</c>, <c>origin</c> и <c>cookie</c> замером НЕ подтверждено: в записанном
/// обмене их не было. Они поставлены после языка — там, где их держат остальные браузеры.
/// </remarks>
public class SafariHeadersFormattingPolicy : HeadersFormattingPolicy
{
    private static readonly IEnumerable<char> defaultPseudoHeadersOrder = ['m', 's', 'a', 'p'];

    /// <summary>Порядок обычных заголовков, снятый с Safari 18.4.</summary>
    private static readonly string[] SafariOrderCommon =
    [
        "sec-fetch-dest",
        "user-agent",
        "accept",
        "sec-fetch-site",
        "sec-fetch-mode",
        "accept-language",

        // Положение этих трёх не подтверждено замером — см. примечание к классу.
        "referer",
        "origin",
        "cookie",

        "priority",
        "accept-encoding",
    ];

    /// <inheritdoc/>
    protected override IEnumerable<string> OrderCommon => SafariOrderCommon;

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
}
