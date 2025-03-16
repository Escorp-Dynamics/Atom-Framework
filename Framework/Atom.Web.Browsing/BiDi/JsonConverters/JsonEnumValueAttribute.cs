namespace Atom.Web.Browsing.BiDi.JsonConverters;

/// <summary>
/// Marks a specific enumerated value with its string representation.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="JsonEnumValueAttribute"/> class.
/// </remarks>
/// <param name="value">The string representation of the enumerated value.</param>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class JsonEnumValueAttribute(string value) : Attribute
{
    /// <summary>
    /// Gets the value to use in JSON serialization and deserialization of the enumerated value.
    /// </summary>
    public string Value { get; } = value;
}