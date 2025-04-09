using System.Text;

namespace Atom.Net.Http.HPack;

internal static class StaticTable
{
    private const string StatusKey = ":status";

    public const int Authority = 1;
    public const int MethodGet = 2;
    public const int MethodPost = 3;
    public const int PathSlash = 4;
    public const int SchemeHttp = 6;
    public const int SchemeHttps = 7;
    public const int Status200 = 8;
    public const int AcceptCharset = 15;
    public const int AcceptEncoding = 16;
    public const int AcceptLanguage = 17;
    public const int AcceptRanges = 18;
    public const int Accept = 19;
    public const int AccessControlAllowOrigin = 20;
    public const int Age = 21;
    public const int Allow = 22;
    public const int Authorization = 23;
    public const int CacheControl = 24;
    public const int ContentDisposition = 25;
    public const int ContentEncoding = 26;
    public const int ContentLanguage = 27;
    public const int ContentLength = 28;
    public const int ContentLocation = 29;
    public const int ContentRange = 30;
    public const int ContentType = 31;
    public const int Cookie = 32;
    public const int Date = 33;
    public const int ETag = 34;
    public const int Expect = 35;
    public const int Expires = 36;
    public const int From = 37;
    public const int Host = 38;
    public const int IfMatch = 39;
    public const int IfModifiedSince = 40;
    public const int IfNoneMatch = 41;
    public const int IfRange = 42;
    public const int IfUnmodifiedSince = 43;
    public const int LastModified = 44;
    public const int Link = 45;
    public const int Location = 46;
    public const int MaxForwards = 47;
    public const int ProxyAuthenticate = 48;
    public const int ProxyAuthorization = 49;
    public const int Range = 50;
    public const int Referer = 51;
    public const int Refresh = 52;
    public const int RetryAfter = 53;
    public const int Server = 54;
    public const int SetCookie = 55;
    public const int StrictTransportSecurity = 56;
    public const int TransferEncoding = 57;
    public const int UserAgent = 58;
    public const int Vary = 59;
    public const int Via = 60;
    public const int WwwAuthenticate = 61;

    public static int Count => s_staticDecoderTable.Length;

    public static ref readonly HeaderField Get(int index) => ref s_staticDecoderTable[index];

    public static bool TryGetStatusIndex(int status, out int index)
    {
        index = status switch
        {
            200 => 8,
            204 => 9,
            206 => 10,
            304 => 11,
            400 => 12,
            404 => 13,
            500 => 14,
            _ => -1
        };

        return index != -1;
    }

    private static readonly HeaderField[] s_staticDecoderTable = [
        CreateHeaderField(1, ":authority", string.Empty),
        CreateHeaderField(2, ":method", "GET"),
        CreateHeaderField(3, ":method", "POST"),
        CreateHeaderField(4, ":path", "/"),
        CreateHeaderField(5, ":path", "/index.html"),
        CreateHeaderField(6, ":scheme", "http"),
        CreateHeaderField(7, ":scheme", "https"),
        CreateHeaderField(8, StatusKey, "200"),
        CreateHeaderField(9, StatusKey, "204"),
        CreateHeaderField(10, StatusKey, "206"),
        CreateHeaderField(11, StatusKey, "304"),
        CreateHeaderField(12, StatusKey, "400"),
        CreateHeaderField(13, StatusKey, "404"),
        CreateHeaderField(14, StatusKey, "500"),
        CreateHeaderField(15, "accept-charset", string.Empty),
        CreateHeaderField(16, "accept-encoding", "gzip, deflate"),
        CreateHeaderField(17, "accept-language", string.Empty),
        CreateHeaderField(18, "accept-ranges", string.Empty),
        CreateHeaderField(19, "accept", string.Empty),
        CreateHeaderField(20, "access-control-allow-origin", string.Empty),
        CreateHeaderField(21, "age", string.Empty),
        CreateHeaderField(22, "allow", string.Empty),
        CreateHeaderField(23, "authorization", string.Empty),
        CreateHeaderField(24, "cache-control", string.Empty),
        CreateHeaderField(25, "content-disposition", string.Empty),
        CreateHeaderField(26, "content-encoding", string.Empty),
        CreateHeaderField(27, "content-language", string.Empty),
        CreateHeaderField(28, "content-length", string.Empty),
        CreateHeaderField(29, "content-location", string.Empty),
        CreateHeaderField(30, "content-range", string.Empty),
        CreateHeaderField(31, "content-type", string.Empty),
        CreateHeaderField(32, "cookie", string.Empty),
        CreateHeaderField(33, "date", string.Empty),
        CreateHeaderField(34, "etag", string.Empty),
        CreateHeaderField(35, "expect", string.Empty),
        CreateHeaderField(36, "expires", string.Empty),
        CreateHeaderField(37, "from", string.Empty),
        CreateHeaderField(38, "host", string.Empty),
        CreateHeaderField(39, "if-match", string.Empty),
        CreateHeaderField(40, "if-modified-since", string.Empty),
        CreateHeaderField(41, "if-none-match", string.Empty),
        CreateHeaderField(42, "if-range", string.Empty),
        CreateHeaderField(43, "if-unmodified-since", string.Empty),
        CreateHeaderField(44, "last-modified", string.Empty),
        CreateHeaderField(45, "link", string.Empty),
        CreateHeaderField(46, "location", string.Empty),
        CreateHeaderField(47, "max-forwards", string.Empty),
        CreateHeaderField(48, "proxy-authenticate", string.Empty),
        CreateHeaderField(49, "proxy-authorization", string.Empty),
        CreateHeaderField(50, "range", string.Empty),
        CreateHeaderField(51, "referer", string.Empty),
        CreateHeaderField(52, "refresh", string.Empty),
        CreateHeaderField(53, "retry-after", string.Empty),
        CreateHeaderField(54, "server", string.Empty),
        CreateHeaderField(55, "set-cookie", string.Empty),
        CreateHeaderField(56, "strict-transport-security", string.Empty),
        CreateHeaderField(57, "transfer-encoding", string.Empty),
        CreateHeaderField(58, "user-agent", string.Empty),
        CreateHeaderField(59, "vary", string.Empty),
        CreateHeaderField(60, "via", string.Empty),
        CreateHeaderField(61, "www-authenticate", string.Empty)
    ];

    private static HeaderField CreateHeaderField(int staticTableIndex, string name, string value) => new(staticTableIndex, Encoding.ASCII.GetBytes(name), value.Length != 0 ? Encoding.ASCII.GetBytes(value) : []);
}