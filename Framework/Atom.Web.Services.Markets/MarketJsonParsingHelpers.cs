using System.Globalization;
using System.Text.Json;

namespace Atom.Web.Services.Markets;

internal static class MarketJsonParsingHelpers
{
    public static string? TryGetString(JsonElement property)
    {
        return property.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => property.GetString(),
            _ => property.ToString()
        };
    }

    public static string? TryGetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
            return null;

        return TryGetString(property);
    }

    public static bool PropertyEquals(
        JsonElement root,
        string propertyName,
        string expected,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        var actual = TryGetString(root, propertyName);
        return string.Equals(actual, expected, comparison);
    }

    public static double? TryParseDouble(JsonElement property)
    {
        var text = TryGetString(property);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    public static double? TryParseDouble(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
            return null;

        return TryParseDouble(property);
    }

    public static int? TryParseInt32(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
            return null;

        return TryParseInt32(property);
    }

    public static int? TryParseInt32(JsonElement property)
    {
        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            _ => null
        };
    }

    public static long? TryParseInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var value) => value,
            JsonValueKind.String when long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            _ => null
        };
    }
}