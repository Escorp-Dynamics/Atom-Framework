using System.Text;
using System.Web;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Строит самоотправляющийся HTML-документ, который выполняет POST-навигацию верхнего уровня.
/// </summary>
/// <remarks>
/// У WebExtension-драйвера нет способа инициировать POST-навигацию документа напрямую:
/// <c lang="text">tabs.update</c> умеет только GET. Единственный честный способ заставить браузер сделать
/// POST на целевой адрес — отдать ему страницу с формой <c lang="text">method="post"</c>, которая
/// отправляется сразу при загрузке. Эта страница подставляется навигационным прокси как ответ
/// на первичный GET, после чего браузер выполняет реальный POST, а перехват его видит.
/// <para>
/// Поддерживается только <c lang="text">application/x-www-form-urlencoded</c>: форма кодирует поля именно
/// так, поэтому тело воспроизводится байт-в-байт. Иные типы тела форма воспроизвести не может,
/// и вызывающий обязан обработать <see langword="false"/> — молча исказить POST нельзя.
/// </para>
/// </remarks>
internal static class BridgePostNavigationForm
{
    internal static bool TryBuild(Uri url, ReadOnlyMemory<byte> body, string? contentType, out string html)
    {
        ArgumentNullException.ThrowIfNull(url);
        html = string.Empty;

        if (!IsFormUrlEncoded(contentType))
            return false;

        if (!TryParseFormFields(body.Span, out var fields))
            return false;

        html = BuildDocument(url, fields);
        return true;
    }

    private static bool IsFormUrlEncoded(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return true;

        var separatorIndex = contentType.IndexOf(';', StringComparison.Ordinal);
        var mediaType = (separatorIndex >= 0 ? contentType[..separatorIndex] : contentType).Trim();
        return mediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseFormFields(ReadOnlySpan<byte> body, out List<KeyValuePair<string, string>> fields)
    {
        fields = [];
        if (body.IsEmpty)
            return true;

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(body);
        }
        catch (ArgumentException)
        {
            return false;
        }

        foreach (var pair in decoded.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equalsIndex = pair.IndexOf('=', StringComparison.Ordinal);
            var rawKey = equalsIndex >= 0 ? pair[..equalsIndex] : pair;
            var rawValue = equalsIndex >= 0 ? pair[(equalsIndex + 1)..] : string.Empty;

            // '+' в urlencoded — это пробел; UrlDecode это учитывает и снимает %XX.
            fields.Add(new KeyValuePair<string, string>(
                HttpUtility.UrlDecode(rawKey, Encoding.UTF8),
                HttpUtility.UrlDecode(rawValue, Encoding.UTF8)));
        }

        return true;
    }

    private static string BuildDocument(Uri url, List<KeyValuePair<string, string>> fields)
    {
        var builder = new StringBuilder();
        builder.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body>");
        builder.Append("<form id=\"atom-post\" method=\"post\" action=\"");
        builder.Append(HttpUtility.HtmlAttributeEncode(url.AbsoluteUri));
        builder.Append("\">");

        foreach (var field in fields)
        {
            builder.Append("<input type=\"hidden\" name=\"");
            builder.Append(HttpUtility.HtmlAttributeEncode(field.Key));
            builder.Append("\" value=\"");
            builder.Append(HttpUtility.HtmlAttributeEncode(field.Value));
            builder.Append("\">");
        }

        builder.Append("</form><script>document.getElementById('atom-post').submit();</script>");
        builder.Append("</body></html>");
        return builder.ToString();
    }
}
