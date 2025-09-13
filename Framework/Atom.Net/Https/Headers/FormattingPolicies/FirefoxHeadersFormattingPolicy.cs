namespace Atom.Net.Https.Headers;

/// <summary>
/// 
/// </summary>
public class FirefoxHeadersFormattingPolicy : HeadersFormattingPolicy
{
    private static readonly IEnumerable<char> defaultPseudoHeadersOrder = ['m', 'p', 'a', 's'];

    /// <inheritdoc/>
    public override IReadOnlyDictionary<RequestKind, IEnumerable<char>> PseudoHeadersOrder { get; set; } = new Dictionary<RequestKind, IEnumerable<char>>
    {
        { RequestKind.Navigation, defaultPseudoHeadersOrder },
        { RequestKind.Preload, defaultPseudoHeadersOrder },
        { RequestKind.Fetch, defaultPseudoHeadersOrder },
        { RequestKind.ServiceWorker, defaultPseudoHeadersOrder },
        { RequestKind.Unknown, defaultPseudoHeadersOrder },
    };
}