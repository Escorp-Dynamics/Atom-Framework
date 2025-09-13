namespace Atom.Net.Https.Headers.HPack;

/// <summary>
/// Статическая таблица HPACK (RFC 7541, Appendix A). Индексация 1..61.
/// </summary>
internal static class HPackStaticTable
{
    public const int Count = 61;

    // Имя/значение для индексов 1..61.
    public static TableEntry Get(int index) => index switch
    {
        1 => new TableEntry(":authority"u8, []),
        2 => new TableEntry(":method"u8, "GET"u8),
        3 => new TableEntry(":method"u8, "POST"u8),
        4 => new TableEntry(":path"u8, "/"u8),
        5 => new TableEntry(":path"u8, "/index.html"u8),
        6 => new TableEntry(":scheme"u8, "http"u8),
        7 => new TableEntry(":scheme"u8, "https"u8),
        8 => new TableEntry(":status"u8, "200"u8),
        9 => new TableEntry(":status"u8, "204"u8),
        10 => new TableEntry(":status"u8, "206"u8),
        11 => new TableEntry(":status"u8, "304"u8),
        12 => new TableEntry(":status"u8, "400"u8),
        13 => new TableEntry(":status"u8, "404"u8),
        14 => new TableEntry(":status"u8, "500"u8),

        15 => new TableEntry("accept-charset"u8, []),
        16 => new TableEntry("accept-encoding"u8, "gzip, deflate"u8),
        17 => new TableEntry("accept-language"u8, []),
        18 => new TableEntry("accept-ranges"u8, []),
        19 => new TableEntry("accept"u8, []),
        20 => new TableEntry("access-control-allow-origin"u8, []),
        21 => new TableEntry("age"u8, []),
        22 => new TableEntry("allow"u8, []),
        23 => new TableEntry("authorization"u8, []),
        24 => new TableEntry("cache-control"u8, []),
        25 => new TableEntry("content-disposition"u8, []),
        26 => new TableEntry("content-encoding"u8, []),
        27 => new TableEntry("content-language"u8, []),
        28 => new TableEntry("content-length"u8, []),
        29 => new TableEntry("content-location"u8, []),
        30 => new TableEntry("content-range"u8, []),
        31 => new TableEntry("content-type"u8, []),
        32 => new TableEntry("cookie"u8, []),
        33 => new TableEntry("date"u8, []),
        34 => new TableEntry("etag"u8, []),
        35 => new TableEntry("expect"u8, []),
        36 => new TableEntry("expires"u8, []),
        37 => new TableEntry("from"u8, []),
        38 => new TableEntry("host"u8, []),
        39 => new TableEntry("if-match"u8, []),
        40 => new TableEntry("if-modified-since"u8, []),
        41 => new TableEntry("if-none-match"u8, []),
        42 => new TableEntry("if-range"u8, []),
        43 => new TableEntry("if-unmodified-since"u8, []),
        44 => new TableEntry("last-modified"u8, []),
        45 => new TableEntry("link"u8, []),
        46 => new TableEntry("location"u8, []),
        47 => new TableEntry("max-forwards"u8, []),
        48 => new TableEntry("proxy-authenticate"u8, []),
        49 => new TableEntry("proxy-authorization"u8, []),
        50 => new TableEntry("range"u8, []),
        51 => new TableEntry("referer"u8, []),
        52 => new TableEntry("refresh"u8, []),
        53 => new TableEntry("retry-after"u8, []),
        54 => new TableEntry("server"u8, []),
        55 => new TableEntry("set-cookie"u8, []),
        56 => new TableEntry("strict-transport-security"u8, []),
        57 => new TableEntry("transfer-encoding"u8, []),
        58 => new TableEntry("user-agent"u8, []),
        59 => new TableEntry("vary"u8, []),
        60 => new TableEntry("via"u8, []),
        61 => new TableEntry("www-authenticate"u8, []),

        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}