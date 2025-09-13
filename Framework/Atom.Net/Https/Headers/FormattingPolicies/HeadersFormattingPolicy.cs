using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Atom.Net.Https.Headers;

/// <summary>
/// Политика форматирования заголовков: порядок, регистр имён, разделение Cookie, псевдозаголовки.
/// </summary>
public abstract class HeadersFormattingPolicy : IHeadersFormattingPolicy
{
    private static readonly Lazy<ChromeHeadersFormattingPolicy> chrome = new(() => new(), true);

    private static readonly Lazy<EdgeHeadersFormattingPolicy> edge = new(() => new(), true);

    private static readonly Lazy<FirefoxHeadersFormattingPolicy> firefox = new(() => new(), true);

    private static readonly Lazy<SafariHeadersFormattingPolicy> safari = new(() => new(), true);

    private static readonly IEnumerable<char> defaultPseudoHeadersOrder = ['m', 's', 'a', 'p'];

    /// <summary>
    /// Максимальная длина одного «Cookie» при дроблении (≈ браузерная практика).
    /// </summary>
    protected virtual int MaxCookieHeaderLength { get; } = 4096;

    /// <summary>
    /// ЕДИНЫЙ master-список сортировки обычных заголовков для всех политик.
    /// Псевдозаголовков здесь нет — они обрабатываются отдельно через PseudoHeadersOrder.
    /// Если какой-то заголовок неактуален для конкретного типа запроса — просто не эмитится.
    /// </summary>
    protected static readonly string[] DefaultOrderCommon = [
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
        "accept",

        // sec-fetch-* (у navigation порядок включает sec-fetch-user)
        "sec-fetch-site",
        "sec-fetch-mode",
        "sec-fetch-user",
        "sec-fetch-dest",

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
        "te",           // Firefox H2: te: trailers
    ];

    /// <inheritdoc/>
    public bool IsMobile { get; init; }

    /// <inheritdoc/>
    public virtual IReadOnlyDictionary<RequestKind, IEnumerable<char>> PseudoHeadersOrder { get; set; } = new Dictionary<RequestKind, IEnumerable<char>>
    {
        { RequestKind.Navigation, defaultPseudoHeadersOrder },
        { RequestKind.Preload, defaultPseudoHeadersOrder },
        { RequestKind.Fetch, defaultPseudoHeadersOrder },
        { RequestKind.ServiceWorker, defaultPseudoHeadersOrder },
        { RequestKind.Unknown, defaultPseudoHeadersOrder },
    };

    /// <summary>
    /// Google Chrome.
    /// </summary>
    public static ChromeHeadersFormattingPolicy Chrome => chrome.Value;

    /// <summary>
    /// Microsoft Edge.
    /// </summary>
    public static EdgeHeadersFormattingPolicy Edge => edge.Value;

    /// <summary>
    /// Mozilla Firefox.
    /// </summary>
    public static FirefoxHeadersFormattingPolicy Firefox => firefox.Value;

    /// <summary>
    /// Apple Safari.
    /// </summary>
    public static SafariHeadersFormattingPolicy Safari => safari.Value;

    /// <summary>
    /// Универсальная реализация форматирования: порядок/регистры/дробление Cookie/фильтры под H2+/тип запроса.
    /// Конкретные политики передают свой orderCommon (обычно <see cref="DefaultOrderCommon"/>).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual IEnumerable<KeyValuePair<string, string>> Format(
        [NotNull] IDictionary<string, string> input,
        [NotNull] Version requestVersion,
        RequestKind requestKind,
        [NotNull] IEnumerable<string> orderCommon,
        bool useCookieCrumbling)
    {
        var isH2Plus = requestVersion.Major >= 2;

        // 1) Эмит известных по master-порядку.
        foreach (var order in orderCommon)
        {
            var nameLower = order;

            if (isH2Plus && IsHopByHopH2(nameLower)) continue;      // host/connection запрещены в H2/H3
            if (!ShouldEmitForKindCore(nameLower, requestKind)) continue;
            if (!TryGetIgnoreCase(input, nameLower, out var value)) continue;

            if (useCookieCrumbling && nameLower == "cookie")
            {
                foreach (var kv in CrumbleCookie(value, isH2Plus)) yield return kv;
            }
            else
            {
                yield return new KeyValuePair<string, string>(
                    NormalizeHeaderNameForWire(nameLower, isH2Plus),
                    value
                );
            }
        }

        // 2) Эмит «хвоста» (входные, не попавшие в orderCommon) — детерминированно, без LINQ.
        foreach (var kv in EnumerateRemainderSorted(input, orderCommon))
        {
            var nameLower = kv.Key; // сравнения далее без понижения регистра/без аллокаций

            if (isH2Plus && IsHopByHopH2(nameLower)) continue;
            if (!ShouldEmitForKindCore(nameLower, requestKind)) continue;

            if (useCookieCrumbling && string.Equals(nameLower, "cookie", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var ckv in CrumbleCookie(kv.Value, isH2Plus)) yield return ckv;
            }
            else
            {
                // Имя нормализуем согласно версии протокола
                var normalizedName = NormalizeHeaderNameForWire(
                    nameLower.ToLowerInvariant(), // безопасно: только одно приведение для имени
                    isH2Plus
                );

                yield return new KeyValuePair<string, string>(normalizedName, kv.Value);
            }
        }
    }

    /// <summary>
    /// В H2+/H3 «host»/«connection» запрещены (браузер использует «:authority» вместо «host»).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool IsHopByHopH2(string name) =>
        string.Equals(name, "host", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "connection", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Минимальные отличия по типу запроса:
    /// - Navigation: все поля из orderCommon допустимы;
    /// - Fetch/Preload/ServiceWorker: не эмитим «upgrade-insecure-requests» и «sec-fetch-user».
    /// При необходимости конкретная политика может переопределить этот предикат.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual bool ShouldEmitForKindCore(string nameLower, RequestKind kind)
    {
        if (kind is RequestKind.Navigation) return true;

        if (string.Equals(nameLower, "upgrade-insecure-requests", StringComparison.OrdinalIgnoreCase)) return default;
        if (string.Equals(nameLower, "sec-fetch-user", StringComparison.OrdinalIgnoreCase)) return default;

        return true;
    }

    /// <summary>
    /// В H/1.1 часть имён Chrome/Firefox оставляют в lowercase (семейство «sec-*», а также «priority»).
    /// Можно переопределить в конкретной политике, если потребуется уточнение.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual bool IsForcedLowercaseH1Core([NotNull] string nameLower)
    {
        if (nameLower.Length >= 4 && nameLower[0] is 's' && nameLower[1] is 'e' && nameLower[2] is 'c' && nameLower[3] is '-') return true;

        return string.Equals(nameLower, "priority", StringComparison.Ordinal);
    }

    /// <summary>Поиск ключа без учёта регистра. Без LINQ, без промежуточных строк.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool TryGetIgnoreCase([NotNull] IDictionary<string, string> src, string lowerName, out string value)
    {
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
        return default;
    }

    /// <summary>
    /// Приведение имени к Title-Case (первая буква и буквы после «-» — заглавные, остальные — строчные).
    /// Короткие имена — через stackalloc; длинные — через массив символов.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static string CanonicalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        Span<char> buffer = stackalloc char[64];

        if (name.Length <= buffer.Length)
        {
            var o = 0;
            var upper = true;

            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];

                if (upper && c >= 'a' && c <= 'z')
                    c = (char)(c - 32);
                else if (!upper && c >= 'A' && c <= 'Z')
                    c = (char)(c + 32);

                buffer[o++] = c;
                upper = c is '-';
            }

            return new string(buffer[..o]);
        }

        var chars = name.ToCharArray();
        var up = true;

        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];

            if (up && c >= 'a' && c <= 'z')
                chars[i] = (char)(c - 32);
            else if (!up && c >= 'A' && c <= 'Z')
                chars[i] = (char)(c + 32);

            up = c is '-';
        }

        return new string(chars);
    }

    /// <summary>
    /// Нормализация имени заголовка для «провода»:
    /// - H2+/H3: строго lowercase;
    /// - H1.1: Title-Case, за исключением семейств «sec-*» и «priority», которые остаются lowercase.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected string NormalizeHeaderNameForWire(string nameLower, bool isH2Plus)
    {
        if (isH2Plus) return nameLower;
        if (IsForcedLowercaseH1Core(nameLower)) return nameLower;
        return CanonicalCase(nameLower);
    }

    /// <summary>
    /// Cookie crumbling: дробим «Cookie» на части ≈4096, режем по «; », не меняя порядок пар.
    /// Имя заголовка нормализуется под текущую версию протокола (H1: Title-Case, H2+/H3: lowercase).
    /// </summary>
    protected IEnumerable<KeyValuePair<string, string>> CrumbleCookie(
        string cookie, bool isH2Plus)
    {
        if (string.IsNullOrEmpty(cookie)) yield break;

        var span = cookie.AsSpan();
        var length = span.Length;
        var start = 0;

        // Имя под «провод»: браузерная мимикрия
        var headerName = isH2Plus ? "cookie" : "Cookie";

        while (start < length)
        {
            var chunkEnd = start;
            var lastSep = -1;

            // собираем кусок до ~4096, режем по "; "
            while (chunkEnd < length)
            {
                if (chunkEnd - start >= MaxCookieHeaderLength) break;
                if (span[chunkEnd] is ';')
                {
                    var next = chunkEnd + 1;
                    if (next < length && span[next] is ' ') lastSep = next;
                }
                chunkEnd++;
            }

            var emitEnd = (chunkEnd >= length) ? length : (lastSep >= 0 ? lastSep + 1 : chunkEnd);
            var slice = span[start..emitEnd].Trim();

            if (!slice.IsEmpty)
                yield return new KeyValuePair<string, string>(headerName, new string(slice));

            start = emitEnd;
            if (start < length && span[start] is ' ') start++;
        }
    }

    /// <summary>
    /// Эмит «хвоста»: пары из входного набора, не вошедшие в master-порядок.
    /// Порядок — лексикографически по имени (OrdinalIgnoreCase), детерминированно и без LINQ.
    /// </summary>
    protected static IEnumerable<KeyValuePair<string, string>> EnumerateRemainderSorted([NotNull] IDictionary<string, string> input, [NotNull] IEnumerable<string> knownOrder)
    {
        static bool ContainsIgnoreCase([NotNull] IEnumerable<string> arr, string name)
        {
            foreach (var item in arr)
            {
                if (string.Equals(item, name, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return default;
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

        // Простая сортировка вставками — объёмы малы, не тащим компараторы/списки.
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