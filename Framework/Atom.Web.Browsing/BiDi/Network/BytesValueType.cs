#pragma warning disable CA1720
using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Network;

/// <summary>
/// The enumerated value of types for a BytesValue.
/// </summary>
[JsonConverter(typeof(EnumValueJsonConverter<BytesValueType>))]
public enum BytesValueType
{
    /// <summary>
    /// The BytesValue represents a string.
    /// </summary>
    String,

    /// <summary>
    /// The BytesValue represents a byte array as a base64-encoded string.
    /// </summary>
    Base64,
}