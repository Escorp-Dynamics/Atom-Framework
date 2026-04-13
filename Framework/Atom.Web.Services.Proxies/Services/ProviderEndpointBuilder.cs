using System.Text;

namespace Atom.Web.Proxies.Services;

internal static class ProviderEndpointBuilder
{
    public static string Create(string baseEndpoint, IReadOnlyDictionary<string, string> query)
    {
        var queryString = CreateQueryString(query);
        return string.IsNullOrEmpty(queryString)
            ? baseEndpoint
            : $"{baseEndpoint}?{queryString}";
    }

    public static string CreateQueryString(IReadOnlyDictionary<string, string> query)
    {
        if (query.Count == 0)
        {
            return string.Empty;
        }

        using var builder = new Atom.Text.ValueStringBuilder(query.Count * 24);
        var isFirst = true;
        foreach (var pair in query)
        {
            if (!isFirst)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(pair.Key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(pair.Value));
            isFirst = false;
        }

        return builder.ToString();
    }

    public static int PositiveOrDefault(int value, int fallback)
        => value > 0 ? value : fallback;

    public static string LowerOrDefault(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();

    public static string UpperOrDefault(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToUpperInvariant();

    public static string PreserveOrDefault(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}