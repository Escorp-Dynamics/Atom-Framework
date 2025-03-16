using System.Text;
using System.Text.Json.Serialization;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// The abstract base class for a value that can contain either a string or a byte array.
/// </summary>
public class BytesValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BytesValue"/> class.
    /// </summary>
    /// <param name="type">The type of value to initialize.</param>
    /// <param name="value">The value to use in the object.</param>
    internal BytesValue(BytesValueType type, string value)
    {
        Type = type;
        Value = value;
    }

    [JsonConstructor]
    internal BytesValue() { }

    /// <summary>
    /// Gets the type of the value object.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [JsonRequired]
    [JsonInclude]
    public BytesValueType Type { get; internal set; } = BytesValueType.String;

    /// <summary>
    /// Gets the value of the value object.
    /// </summary>
    [JsonRequired]
    [JsonInclude]
    public string Value { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the value of the value object as an array of bytes.
    /// </summary>
    [JsonIgnore]
    public ReadOnlyMemory<byte> ValueAsByteArray => Type is BytesValueType.String ? Encoding.UTF8.GetBytes(Value) : Convert.FromBase64String(Value);

    /// <summary>
    /// Creates a BytesValue object from a string value.
    /// </summary>
    /// <param name="stringValue">The string value the BytesValue contains.</param>
    /// <returns>The BytesValue representing the string.</returns>
    public static BytesValue FromString(string stringValue) => new(BytesValueType.String, stringValue);

    /// <summary>
    /// Creates a BytesValue object from a base64-encoded string value.
    /// </summary>
    /// <param name="base64Value">The value of the BytesValue as a base64-encoded string.</param>
    /// <returns>The BytesValue representing the value.</returns>
    public static BytesValue FromBase64String(string base64Value) => new(BytesValueType.Base64, base64Value);

    /// <summary>
    /// Creates a BytesValue object containing a base64-encoded string from an array of bytes.
    /// </summary>
    /// <param name="bytes">The value of the BytesValue as a byte array.</param>
    /// <returns>The BytesValue representing the value.</returns>
    public static BytesValue FromByteArray(byte[] bytes) => new(BytesValueType.Base64, Convert.ToBase64String(bytes));
}