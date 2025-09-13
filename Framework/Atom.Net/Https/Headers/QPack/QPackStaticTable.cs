using System.Runtime.CompilerServices;

namespace Atom.Net.Https.Headers.QPack;

internal static class QPackStaticTable
{
    /// <summary>
    /// Кол-во записей в статике QPACK v1 (RFC 9204, Appendix A) — 99.
    /// </summary>
    public const int Count = 99;

    /// <summary>
    /// Линейный поиск индекса по имени (без аллокаций). Возвращает первый индекс с таким именем или -1.
    /// Учитывает только ASCII‑имена.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int TryFindNameIndex(scoped ReadOnlySpan<char> lowerAsciiName)
    {
        for (var i = 0; i < Count; i++)
        {
            var e = Get(i);
            if (NameEquals(lowerAsciiName, e.Name)) return i;
        }

        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool NameEquals(scoped ReadOnlySpan<char> a, ReadOnlySpan<byte> bLowerAscii)
    {
        if (a.Length != bLowerAscii.Length) return default;

        for (var i = 0; i < a.Length; i++)
        {
            var c = a[i];

            if (c > 0x7F) return default;
            if (c is >= 'A' and <= 'Z') c = (char)(c + 32);
            if ((byte)c != bLowerAscii[i]) return default;
        }

        return true;
    }

    /// <summary>
    /// Получить элемент статической таблицы по индексу (0..98). Пока бросаем исключение,
    /// чтобы не допустить десинхронизации; заполним позже.
    /// </summary>
    public static TableEntry Get(int index) => index switch
    {
        0 => new TableEntry(":authority"u8, []),
        1 => new TableEntry(":path"u8, "/"u8),
        2 => new TableEntry("age"u8, []),
        3 => new TableEntry("content-disposition"u8, []),
        4 => new TableEntry("content-length"u8, []),
        5 => new TableEntry("cookie"u8, []),
        6 => new TableEntry("date"u8, []),
        7 => new TableEntry("etag"u8, []),
        8 => new TableEntry("if-modified-since"u8, []),
        9 => new TableEntry("if-none-match"u8, []),
        10 => new TableEntry("last-modified"u8, []),
        11 => new TableEntry("link"u8, []),
        12 => new TableEntry("location"u8, []),
        13 => new TableEntry("referer"u8, []),
        14 => new TableEntry("set-cookie"u8, []),

        15 => new TableEntry(":method"u8, "CONNECT"u8),
        16 => new TableEntry(":method"u8, "DELETE"u8),
        17 => new TableEntry(":method"u8, "GET"u8),
        18 => new TableEntry(":method"u8, "HEAD"u8),
        19 => new TableEntry(":method"u8, "OPTIONS"u8),
        20 => new TableEntry(":method"u8, "POST"u8),
        21 => new TableEntry(":method"u8, "PUT"u8),
        22 => new TableEntry(":scheme"u8, "http"u8),
        23 => new TableEntry(":scheme"u8, "https"u8),
        24 => new TableEntry(":status"u8, "103"u8),
        25 => new TableEntry(":status"u8, "200"u8),
        26 => new TableEntry(":status"u8, "304"u8),
        27 => new TableEntry(":status"u8, "404"u8),
        28 => new TableEntry(":status"u8, "503"u8),

        29 => new TableEntry("accept"u8, "*/*"u8),
        30 => new TableEntry("accept"u8, "application/dns-message"u8),
        31 => new TableEntry("accept-encoding"u8, "gzip, deflate, br"u8),
        32 => new TableEntry("accept-ranges"u8, "bytes"u8),
        33 => new TableEntry("access-control-allow-headers"u8, "cache-control"u8),
        34 => new TableEntry("access-control-allow-headers"u8, "content-type"u8),
        35 => new TableEntry("access-control-allow-origin"u8, "*"u8),
        36 => new TableEntry("cache-control"u8, "max-age=0"u8),
        37 => new TableEntry("cache-control"u8, "max-age=2592000"u8),
        38 => new TableEntry("cache-control"u8, "max-age=604800"u8),
        39 => new TableEntry("cache-control"u8, "no-cache"u8),
        40 => new TableEntry("cache-control"u8, "no-store"u8),
        41 => new TableEntry("cache-control"u8, "public, max-age=31536000"u8),
        42 => new TableEntry("content-encoding"u8, "br"u8),
        43 => new TableEntry("content-encoding"u8, "gzip"u8),
        44 => new TableEntry("content-type"u8, "application/dns-message"u8),
        45 => new TableEntry("content-type"u8, "application/javascript"u8),
        46 => new TableEntry("content-type"u8, "application/json"u8),
        47 => new TableEntry("content-type"u8, "application/x-www-form-urlencoded"u8),
        48 => new TableEntry("content-type"u8, "image/gif"u8),
        49 => new TableEntry("content-type"u8, "image/jpeg"u8),
        50 => new TableEntry("content-type"u8, "image/png"u8),
        51 => new TableEntry("content-type"u8, "text/css"u8),
        52 => new TableEntry("content-type"u8, "text/html; charset=utf-8"u8),
        53 => new TableEntry("content-type"u8, "text/plain"u8),
        54 => new TableEntry("content-type"u8, "text/plain;charset=utf-8"u8),
        55 => new TableEntry("range"u8, "bytes=0-"u8),
        56 => new TableEntry("strict-transport-security"u8, "max-age=31536000"u8),
        57 => new TableEntry("strict-transport-security"u8, "max-age=31536000; includesubdomains"u8),
        58 => new TableEntry("strict-transport-security"u8, "max-age=31536000; includesubdomains; preload"u8),
        59 => new TableEntry("vary"u8, "accept-encoding"u8),
        60 => new TableEntry("vary"u8, "origin"u8),
        61 => new TableEntry("x-content-type-options"u8, "nosniff"u8),
        62 => new TableEntry("x-xss-protection"u8, "1; mode=block"u8),

        63 => new TableEntry(":status"u8, "100"u8),
        64 => new TableEntry(":status"u8, "204"u8),
        65 => new TableEntry(":status"u8, "206"u8),
        66 => new TableEntry(":status"u8, "302"u8),
        67 => new TableEntry(":status"u8, "400"u8),
        68 => new TableEntry(":status"u8, "403"u8),
        69 => new TableEntry(":status"u8, "421"u8),
        70 => new TableEntry(":status"u8, "425"u8),
        71 => new TableEntry(":status"u8, "500"u8),

        72 => new TableEntry("accept-language"u8, []),
        73 => new TableEntry("access-control-allow-credentials"u8, "TRUE"u8), // см. errata; оставляем как в RFC 9204
        74 => new TableEntry("access-control-allow-credentials"u8, "FALSE"u8),
        75 => new TableEntry("access-control-allow-headers"u8, "*"u8),
        76 => new TableEntry("access-control-allow-methods"u8, "get"u8),
        77 => new TableEntry("access-control-allow-methods"u8, "get, post, options"u8),
        78 => new TableEntry("access-control-allow-methods"u8, "options"u8),
        79 => new TableEntry("access-control-expose-headers"u8, "content-length"u8),
        80 => new TableEntry("access-control-request-headers"u8, "content-type"u8),
        81 => new TableEntry("access-control-request-method"u8, "get"u8),
        82 => new TableEntry("access-control-request-method"u8, "post"u8),
        83 => new TableEntry("alt-svc"u8, "clear"u8),
        84 => new TableEntry("authorization"u8, []),
        85 => new TableEntry("content-security-policy"u8, "script-src 'none'; object-src 'none'; base-uri 'none'"u8),
        86 => new TableEntry("early-data"u8, "1"u8),
        87 => new TableEntry("expect-ct"u8, "max-age=0"u8),
        88 => new TableEntry("origin"u8, []),
        89 => new TableEntry("purpose"u8, "prefetch"u8),
        90 => new TableEntry("server"u8, []),
        91 => new TableEntry("timing-allow-origin"u8, "*"u8),
        92 => new TableEntry("upgrade-insecure-requests"u8, "1"u8),
        93 => new TableEntry("user-agent"u8, []),
        94 => new TableEntry("x-forwarded-for"u8, []),
        95 => new TableEntry("x-frame-options"u8, "deny"u8),
        96 => new TableEntry("x-frame-options"u8, "sameorigin"u8),
        97 => new TableEntry(":path"u8, "/index.html"u8),
        98 => new TableEntry("content-type"u8, "image/svg+xml"u8),

        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}