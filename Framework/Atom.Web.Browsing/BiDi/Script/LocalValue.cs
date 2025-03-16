#pragma warning disable CA1720
using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Object representing a local value for use as an argument in script execution.
/// </summary>
public class LocalValue : ArgumentValue
{
    private LocalValue(string argType) => Type = argType;

    /// <summary>
    /// Gets a LocalValue for "undefined".
    /// </summary>
    public static LocalValue Undefined => new("undefined");

    /// <summary>
    /// Gets a LocalValue for a null value.
    /// </summary>
    public static LocalValue Null => new("null");

    /// <summary>
    /// Gets a LocalValue for "NaN".
    /// </summary>
    public static LocalValue NaN => new("number") { Value = double.NaN };

    /// <summary>
    /// Gets a LocalValue for negative zero (-0).
    /// </summary>
    public static LocalValue NegativeZero => new("number") { Value = decimal.Negate(decimal.Zero) };

    /// <summary>
    /// Gets a LocalValue for positive infinity.
    /// </summary>
    public static LocalValue Infinity => new("number") { Value = double.PositiveInfinity };

    /// <summary>
    /// Gets a LocalValue for negative infinity.
    /// </summary>
    public static LocalValue NegativeInfinity => new("number") { Value = double.NegativeInfinity };

    /// <summary>
    /// Gets the type of this LocalValue.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; private set; }

    /// <summary>
    /// Gets the object containing the value of this LocalValue.
    /// </summary>
    [JsonIgnore]
    public object? Value { get; private set; }

    /// <summary>
    /// Gets the object containing the value of this LocalValue for serialization purposes.
    /// </summary>
    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    internal object? SerializableValue
    {
        get
        {
            if (Type is "number") return GetSerializedNumericValue();
            if (Type is "bigint") return ((BigInteger)Value!).ToString(CultureInfo.InvariantCulture);
            if (Type is "date") return ((DateTime)Value!).ToString("YYYY-MM-ddTHH:mm:ss.fffZ");

            if (Type is "map" or "object")
            {
                var serializablePairList = new List<object>();
                var dictionaryValue = Value as Dictionary<LocalValue, LocalValue>;
                var stringDictionaryValue = Value as Dictionary<string, LocalValue>;

                if (dictionaryValue is not null)
                {
                    foreach (var pair in dictionaryValue)
                    {
                        List<object> itemList = [pair.Key, pair.Value];
                        serializablePairList.Add(itemList);
                    }
                }

                if (stringDictionaryValue is not null)
                {
                    foreach (var pair in stringDictionaryValue)
                    {
                        List<object> itemList = [pair.Key, pair.Value];
                        serializablePairList.Add(itemList);
                    }
                }

                return serializablePairList;
            }

            return Value;
        }
    }

    /// <summary>
    /// Creates a LocalValue for a string.
    /// </summary>
    /// <param name="stringValue">The string to wrap as a LocalValue.</param>
    /// <returns>A LocalValue for a string.</returns>
    public static LocalValue String(string stringValue) => new("string") { Value = stringValue };

    /// <summary>
    /// Creates a LocalValue for a number.
    /// </summary>
    /// <param name="numericValue">The integer to wrap as a LocalValue.</param>
    /// <returns>A LocalValue for a number.</returns>
    public static LocalValue Number(int numericValue) => new("number") { Value = numericValue };

    /// <summary>
    /// Creates a LocalValue for a number.
    /// </summary>
    /// <param name="numericValue">The long to wrap as a LocalValue.</param>
    /// <returns>A LocalValue for a number.</returns>
    public static LocalValue Number(long numericValue) => new("number") { Value = numericValue };

    /// <summary>
    /// Creates a LocalValue for a number.
    /// </summary>
    /// <param name="numericValue">The double to wrap as a LocalValue.</param>
    /// <returns>A LocalValue for a number.</returns>
    public static LocalValue Number(double numericValue) => new("number") { Value = numericValue };

    /// <summary>
    /// Creates a LocalValue for a number.
    /// </summary>
    /// <param name="numericValue">The decimal to wrap as a LocalValue.</param>
    /// <returns>A LocalValue for a number.</returns>
    public static LocalValue Number(decimal numericValue) => new("number") { Value = numericValue };

    /// <summary>
    /// Creates a LocalValue for a boolean value.
    /// </summary>
    /// <param name="boolValue">The boolean to wrap as a LocalValue.</param>
    /// <returns>A LocalValue for a boolean value.</returns>
    public static LocalValue Boolean(bool boolValue) => new("boolean") { Value = boolValue };

    /// <summary>
    /// Creates a LocalValue for a BigInteger.
    /// </summary>
    /// <param name="bigIntValue">The BigInteger to wrap as a LocalValue.</param>
    /// <returns>A LocalValue for a BigInteger.</returns>
    public static LocalValue BigInt(BigInteger bigIntValue) => new("bigint") { Value = bigIntValue };

    /// <summary>
    /// Creates a LocalValue for a DateTime value.
    /// </summary>
    /// <param name="dateTimeValue">The DateTime value to wrap as a LocalValue.</param>
    /// <returns>A LocalValue for a DateTime value.</returns>
    public static LocalValue Date(DateTime dateTimeValue) => new("date") { Value = dateTimeValue };

    /// <summary>
    /// Creates a LocalValue for an array.
    /// </summary>
    /// <param name="arrayValue">The list of LocalValues to wrap as an array LocalValue.</param>
    /// <returns>A LocalValue for an array.</returns>
    public static LocalValue Array(IEnumerable<LocalValue> arrayValue) => new("array") { Value = arrayValue };

    /// <summary>
    /// Creates a LocalValue for a set.
    /// </summary>
    /// <param name="arrayValue">The list of LocalValues to wrap as a set LocalValue.</param>
    /// <returns>A LocalValue for a set.</returns>
    public static LocalValue Set(IEnumerable<LocalValue> arrayValue) => new("set") { Value = arrayValue };

    /// <summary>
    /// Creates a LocalValue for a map with string keys.
    /// </summary>
    /// <param name="mapValue">The dictionary with strings for keys and LocalValues for values to wrap as a map LocalValue.</param>
    /// <returns>A LocalValue for a map.</returns>
    public static LocalValue Map(IDictionary<string, LocalValue> mapValue) => new("map") { Value = mapValue };

    /// <summary>
    /// Creates a LocalValue for a map with LocalValue keys.
    /// </summary>
    /// <param name="mapValue">The dictionary with LocalValues for keys and LocalValues for values to wrap as a map LocalValue.</param>
    /// <returns>A LocalValue for a map.</returns>
    public static LocalValue Map(IDictionary<LocalValue, LocalValue> mapValue) => new("map") { Value = mapValue };

    /// <summary>
    /// Creates a LocalValue for an object with string keys.
    /// </summary>
    /// <param name="mapValue">The dictionary with strings for keys and LocalValues for values to wrap as an object LocalValue.</param>
    /// <returns>A LocalValue for an object.</returns>
    public static LocalValue Object(IDictionary<string, LocalValue> mapValue) => new("object") { Value = mapValue };

    /// <summary>
    /// Creates a LocalValue for an object with LocalValue keys.
    /// </summary>
    /// <param name="mapValue">The dictionary with LocalValues for keys and LocalValues for values to wrap as an object LocalValue.</param>
    /// <returns>A LocalValue for an object.</returns>
    public static LocalValue Object(IDictionary<LocalValue, LocalValue> mapValue) => new("object") { Value = mapValue };

    /// <summary>
    /// Creates a LocalValue for regular expression.
    /// </summary>
    /// <param name="pattern">The pattern for the regular expression.</param>
    /// <param name="flags">The flags of the regular expression.</param>
    /// <returns>A LocalValue for regular expression.</returns>
    public static LocalValue RegExp(string pattern, string? flags = null) => new("regexp") { Value = new RegularExpressionValue(pattern, flags) };

    private object? GetSerializedNumericValue()
    {
        var doubleValue = Value as double?;

        if (doubleValue is not null && doubleValue.HasValue)
        {
            if (double.IsNaN(doubleValue.Value))
                return "NaN";
            else if (double.IsPositiveInfinity(doubleValue.Value))
                return "Infinity";
            else if (double.IsNegativeInfinity(doubleValue.Value))
                return "-Infinity";
        }

        var decimalValue = Value as decimal?;
        return decimalValue is not null && decimalValue.HasValue && decimalValue.Value == decimal.Negate(decimal.Zero) ? "-0" : Value;
    }
}