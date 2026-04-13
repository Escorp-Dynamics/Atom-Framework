using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Atom.Net.Https.Headers;

/// <summary>
/// Политика форматирования заголовков: порядок, регистр имён, разделение Cookie, псевдозаголовки.
/// Упрощённая и рефакторированная реализация: разделение длинных методов на помощники,
/// отказ от Span/ReadOnlySpan в методах-итераторах и соблюдение правил .editorconfig.
/// </summary>
public abstract class HeadersFormattingPolicy : IHeadersFormattingPolicy
{
    private static readonly Lazy<ChromeHeadersFormattingPolicy> chrome = new(() => new(), isThreadSafe: true);
    private static readonly Lazy<EdgeHeadersFormattingPolicy> edge = new(() => new(), isThreadSafe: true);
    private static readonly Lazy<FirefoxHeadersFormattingPolicy> firefox = new(() => new(), isThreadSafe: true);
    private static readonly Lazy<SafariHeadersFormattingPolicy> safari = new(() => new(), isThreadSafe: true);
    private static readonly IEnumerable<char> defaultPseudoHeadersOrder = ['m', 's', 'a', 'p'];

    protected virtual int MaxCookieHeaderLength { get; } = 4096;

    protected static readonly string[] DefaultOrderCommon =
    [
        // NB: Для H2+/H3 host/connection не эмитим (см. IsHopByHopH2)
        "host",
        "connection",

        // Client Hints и сетевые метрики (Chromium/Edge/Chrome навигация)
        "device-memory",
        "sec-ch-device-memory",
        "dpr",
        "sec-ch-dpr",
        "viewport-width",
        "sec-ch-viewport-width",
        "sec-ch-viewport-height",
        "rtt",
        "downlink",
        "ect",

        // UA-блок
        "sec-ch-ua",
        "sec-ch-ua-mobile",
        "sec-ch-ua-full-version",
        "sec-ch-ua-arch",
        "sec-ch-ua-platform",
        "sec-ch-ua-platform-version",
        "sec-ch-ua-bitness",
        "sec-ch-ua-model",
        "sec-ch-ua-wow64",
        "sec-ch-ua-full-version-list",
        "sec-ch-ua-form-factors",
        "sec-ch-prefers-color-scheme",
        "sec-ch-prefers-reduced-motion",
        "sec-ch-prefers-reduced-transparency",

        // Навигационные (Navigation-only), фильтруются предикатом по типу
        "upgrade-insecure-requests",

        // Базовые
        "user-agent",
        "sec-purpose",
        "accept",

        // sec-fetch-* (у navigation порядок включает sec-fetch-user)
        "sec-fetch-site",
        "sec-fetch-mode",
        "sec-fetch-user",
        "sec-fetch-dest",
        "service-worker",

        // Ссылочные/кросс-сайтовые
        "referer",
        "origin",

        // Прочее
        "accept-encoding",
        "accept-language",
        "cookie",
        "priority",     // Chromium: встречается и на H1, и на H2/H3

        // Для совместимости с Firefox (HTTP/2)
        "dnt",
        "pragma",
        "cache-control",
        "te",
    ];

    /// <inheritdoc/>
    public bool IsMobile { get; init; }

    /// <inheritdoc/>
    public virtual IReadOnlyDictionary<RequestKind, IEnumerable<char>> PseudoHeadersOrder { get; set; } =
        new Dictionary<RequestKind, IEnumerable<char>>
        {
            { RequestKind.Navigation, defaultPseudoHeadersOrder },
            { RequestKind.Preload, defaultPseudoHeadersOrder },
            { RequestKind.ModulePreload, defaultPseudoHeadersOrder },
            { RequestKind.Prefetch, defaultPseudoHeadersOrder },
            { RequestKind.Fetch, defaultPseudoHeadersOrder },
            { RequestKind.ServiceWorker, defaultPseudoHeadersOrder },
            { RequestKind.Unknown, defaultPseudoHeadersOrder },
        };

    public static ChromeHeadersFormattingPolicy Chrome => chrome.Value;
    public static EdgeHeadersFormattingPolicy Edge => edge.Value;
    public static FirefoxHeadersFormattingPolicy Firefox => firefox.Value;
    public static SafariHeadersFormattingPolicy Safari => safari.Value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual IEnumerable<KeyValuePair<string, string>> Format(
        IDictionary<string, string> input,
        Version requestVersion,
        RequestKind requestKind,
        IEnumerable<string> orderCommon,
        bool useCookieCrumbling)
    {
        // Argument validation must not be performed inside iterator methods.
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(requestVersion);
        ArgumentNullException.ThrowIfNull(orderCommon);

        return FormatIterator(input, requestVersion, requestKind, orderCommon, useCookieCrumbling);
    }

    private IEnumerable<KeyValuePair<string, string>> FormatIterator(
        IDictionary<string, string> input,
        Version requestVersion,
        RequestKind requestKind,
        IEnumerable<string> orderCommon,
        bool useCookieCrumbling)
    {
        var isH2Plus = requestVersion.Major >= 2;

        foreach (var kv in FormatKnownHeaders(input, requestKind, orderCommon, useCookieCrumbling, isH2Plus))
            yield return kv;

        foreach (var kv in FormatRemainderHeaders(input, requestKind, orderCommon, useCookieCrumbling, isH2Plus))
            yield return kv;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool IsHopByHopH2(string name) =>
        string.Equals(name, "host", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "connection", StringComparison.OrdinalIgnoreCase);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual bool ShouldEmitForKindCore(string nameLower, RequestKind kind)
    {
        if (kind is RequestKind.Navigation) return true;

        if (string.Equals(nameLower, "upgrade-insecure-requests", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(nameLower, "sec-fetch-user", StringComparison.OrdinalIgnoreCase)) return false;

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual bool IsForcedLowercaseH1Core(string nameLower)
    {
        ArgumentNullException.ThrowIfNull(nameLower);

        if (nameLower.Length >= 4 && nameLower[0] is 's' && nameLower[1] is 'e' && nameLower[2] is 'c' && nameLower[3] is '-')
            return true;

        return string.Equals(nameLower, "priority", StringComparison.Ordinal);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool TryGetIgnoreCase(IDictionary<string, string> src, string lowerName, out string value)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(lowerName);

        if (src.TryGetValue(lowerName, out value!)) return true;

        foreach (var kv in src)
        {
            if (string.Equals(kv.Key, lowerName, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static string CanonicalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (name.Length <= 64) return CanonicalCaseStack(name);
        return CanonicalCaseHeap(name);
    }

    private static string CanonicalCaseStack(string name)
    {
        Span<char> buffer = stackalloc char[64];
        var o = 0;
        var upper = true;
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (upper && c >= 'a' && c <= 'z') c = (char)(c - 32);
            else if (!upper && c >= 'A' && c <= 'Z') c = (char)(c + 32);
            buffer[o++] = c;
            upper = c is '-';
        }

        return new string(buffer[..o]);
    }

    private static string CanonicalCaseHeap(string name)
    {
        var chars = name.ToCharArray();
        var up = true;
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (up && c >= 'a' && c <= 'z') chars[i] = (char)(c - 32);
            else if (!up && c >= 'A' && c <= 'Z') chars[i] = (char)(c + 32);
            up = c is '-';
        }

        return new string(chars);
    }

    private IEnumerable<KeyValuePair<string, string>> FormatKnownHeaders(
        IDictionary<string, string> input,
        RequestKind requestKind,
        IEnumerable<string> orderCommon,
        bool useCookieCrumbling,
        bool isH2Plus)
    {
        foreach (var order in orderCommon)
        {
            var nameLower = order;
            if (isH2Plus && IsHopByHopH2(nameLower)) continue;
            if (!ShouldEmitForKindCore(nameLower, requestKind)) continue;
            if (!TryGetIgnoreCase(input, nameLower, out var value)) continue;
            if (useCookieCrumbling && string.Equals(nameLower, "cookie", StringComparison.OrdinalIgnoreCase))
            {
                var crumbled = CrumbleCookie(value, isH2Plus);
                foreach (var kv in crumbled) yield return kv;
            }
            else
            {
                yield return new KeyValuePair<string, string>(NormalizeHeaderNameForWire(nameLower, isH2Plus), value);
            }
        }
    }

    private IEnumerable<KeyValuePair<string, string>> FormatRemainderHeaders(
        IDictionary<string, string> input,
        RequestKind requestKind,
        IEnumerable<string> orderCommon,
        bool useCookieCrumbling,
        bool isH2Plus)
    {
        foreach (var kv in EnumerateRemainderSorted(input, orderCommon))
        {
            var nameLower = kv.Key;
            if (isH2Plus && IsHopByHopH2(nameLower)) continue;
            if (!ShouldEmitForKindCore(nameLower, requestKind)) continue;
            if (useCookieCrumbling && string.Equals(nameLower, "cookie", StringComparison.OrdinalIgnoreCase))
            {
                var crumbled = CrumbleCookie(kv.Value, isH2Plus);
                foreach (var ckv in crumbled) yield return ckv;
            }
            else
            {
                var normalizedName = NormalizeHeaderNameForWire(nameLower.ToLowerInvariant(), isH2Plus);
                yield return new KeyValuePair<string, string>(normalizedName, kv.Value);
            }
        }
    }

    protected string NormalizeHeaderNameForWire(string nameLower, bool isH2Plus)
    {
        if (isH2Plus) return nameLower;
        if (IsForcedLowercaseH1Core(nameLower)) return nameLower;
        return CanonicalCase(nameLower);
    }

    protected IReadOnlyList<KeyValuePair<string, string>> CrumbleCookie(string cookie, bool isH2Plus)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrEmpty(cookie)) return result;
        var length = cookie.Length;
        var start = 0;
        var headerName = isH2Plus ? "cookie" : "Cookie";
        while (start < length)
        {
            var chunkEnd = start;
            var lastSep = -1;
            for (var i = start; i < length; i++)
            {
                if (i - start >= MaxCookieHeaderLength) break;
                if (cookie[i] == ';')
                {
                    var next = i + 1;
                    if (next < length && cookie[next] == ' ') lastSep = next;
                }
                chunkEnd = i + 1;
            }

            var emitEnd = chunkEnd >= length
                ? length
                : lastSep >= 0
                    ? lastSep + 1
                    : chunkEnd;

            var s = start;
            var e = emitEnd - 1;
            while (s <= e && cookie[s] == ' ') s++;
            while (e >= s && cookie[e] == ' ') e--;
            var sliceLen = e - s + 1;
            if (sliceLen > 0)
            {
                var slice = cookie.Substring(s, sliceLen);
                result.Add(new KeyValuePair<string, string>(headerName, slice));
            }

            start = emitEnd;
            if (start < length && cookie[start] == ' ') start++;
        }

        return result;
    }

    protected static IEnumerable<KeyValuePair<string, string>> EnumerateRemainderSorted(IDictionary<string, string> input, IEnumerable<string> knownOrder)
    {
        // Validate arguments outside the iterator implementation.
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(knownOrder);

        return EnumerateRemainderSortedIterator(input, knownOrder);
    }

    private static IEnumerable<KeyValuePair<string, string>> EnumerateRemainderSortedIterator(IDictionary<string, string> input, IEnumerable<string> knownOrder)
    {
        static bool ContainsIgnoreCase(IEnumerable<string> arr, string name)
        {
            ArgumentNullException.ThrowIfNull(arr);
            ArgumentNullException.ThrowIfNull(name);
            foreach (var item in arr)
            {
                if (string.Equals(item, name, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        var count = 0;
        foreach (var kv in input)
        {
            if (!ContainsIgnoreCase(knownOrder, kv.Key)) count++;
        }

        if (count is 0) yield break;

        var arr = new KeyValuePair<string, string>[count];
        var o = 0;
        foreach (var kv in input)
        {
            if (!ContainsIgnoreCase(knownOrder, kv.Key)) arr[o++] = kv;
        }

        for (var i = 1; i < arr.Length; i++)
        {
            var cur = arr[i];
            var j = i - 1;
            while (j >= 0 && string.Compare(arr[j].Key, cur.Key, StringComparison.OrdinalIgnoreCase) > 0)
            {
                arr[j + 1] = arr[j];
                j--;
            }

            arr[j + 1] = cur;
        }

        for (var i = 0; i < arr.Length; i++) yield return arr[i];
    }

    /// <inheritdoc/>
    public IEnumerable<KeyValuePair<string, string>> Format(IDictionary<string, string> input, Version requestVersion, RequestKind requestKind, bool useCookieCrumbling) => Format(input, requestVersion, requestKind, DefaultOrderCommon, useCookieCrumbling);
}