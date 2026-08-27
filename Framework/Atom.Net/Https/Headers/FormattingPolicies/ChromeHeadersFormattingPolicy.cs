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
}