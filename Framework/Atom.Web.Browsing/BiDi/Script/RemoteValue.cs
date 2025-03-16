using System.Text.Json.Serialization;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Script;

/// <summary>
/// Object representing a remote value in the browser.
/// </summary>
[JsonConverter(typeof(RemoteValueJsonConverter))]
public class RemoteValue
{
    private static readonly List<string> KnownRemoteValueTypes = [
        "undefined",
        "null",
        "string",
        "number",
        "boolean",
        "bigint",
        "symbol",
        "array",
        "object",
        "function",
        "regexp",
        "date",
        "map",
        "set",
        "weakmap",
        "weakset",
        "generator",
        "error",
        "proxy",
        "promise",
        "typedarray",
        "arraybuffer",
        "nodelist",
        "htmlcollection",
        "node",
        "window",
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoteValue"/> class.
    /// </summary>
    /// <param name="valueType">The string describing the type of this RemoteValue.</param>
    [JsonConstructor]
    internal RemoteValue(string valueType) => Type = valueType;

    /// <summary>
    /// Gets the type of this RemoteValue.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; private set; }

    /// <summary>
    /// Gets the handle of this RemoteValue.
    /// </summary>
    [JsonPropertyName("handle")]
    public string? Handle { get; internal set; }

    /// <summary>
    /// Gets the internal ID of this RemoteValue.
    /// </summary>
    [JsonPropertyName("internalId")]
    public string? InternalId { get; internal set; }

    /// <summary>
    /// Gets the shared ID of this RemoteValue.
    /// </summary>
    [JsonPropertyName("sharedId")]
    public string? SharedId { get; internal set; }

    /// <summary>
    /// Gets the object that contains the value of this RemoteValue.
    /// </summary>
    [JsonPropertyName("value")]
    public object? Value { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether this RemoteValue has a value.
    /// </summary>
    [JsonIgnore]
    public bool HasValue => this.Value is not null;

    /// <summary>
    /// Gets a value indicating whether this RemoteValue contains a primitive value.
    /// </summary>
    [JsonIgnore]
    internal bool IsPrimitive => Type is "string" or "number" or "boolean" or "bigint" or "null" or "undefined";

    /// <summary>
    /// Gets a value indicating whether the specified type is valid for creating a RemoteValue.
    /// </summary>
    /// <param name="type">The type to check for validity.</param>
    /// <returns><see langword="true"/> if the value is valid for creating a RemoteValue; otherwise, <see langword="false"/>.</returns>
    public static bool IsValidRemoteValueType(string type) => KnownRemoteValueTypes.Contains(type);

    /// <summary>
    /// Gets the value of this RemoteValue cast to the desired type.
    /// </summary>
    /// <typeparam name="T">The type to which to cast the value object.</typeparam>
    /// <returns>The value cast to the desired type.</returns>
    /// <exception cref="BiDiException">Thrown if this RemoteValue cannot be cast to the desired type.</exception>
    public T? ValueAs<T>()
    {
        T? result = default;
        var type = typeof(T);

        if (Value is null)
        {
            if (type.IsValueType) throw new BiDiException("RemoteValue has null value, but desired type is a value type");
        }
        else
        {
            result = !type.IsInstanceOfType(Value)
                ? throw new BiDiException("RemoteValue could not be cast to the desired type")
                : (T)Value;
        }

        return result;
    }

    /// <summary>
    /// Converts this RemoteValue into a RemoteReference.
    /// </summary>
    /// <returns>The RemoteReference object representing this RemoteValue.</returns>
    /// <exception cref="BiDiException">
    /// Thrown when the RemoteValue meets one of the following conditions:
    /// <list type="bulleted">
    ///   <item>
    ///     <description>
    ///       The RemoteValue is a primitive value (string, number, boolean, bigint, null, or undefined)
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       The RemoteValue has a type of "node", but there is no shared ID set
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       The RemoteValue does not have a handle set
    ///     </description>
    ///   </item>
    /// </list>
    /// </exception>
    public RemoteReference ToRemoteReference()
    {
        if (IsPrimitive) throw new BiDiException("Primitive values cannot be used as remote references");

        if (Type is "node")
        {
            return SharedId is null
                ? throw new BiDiException("Node remote values must have a valid shared ID to be used as remote references")
                : new SharedReference(SharedId) { Handle = Handle };
        }

        return Handle is null
            ? throw new BiDiException("Remote values must have a valid handle to be used as remote references")
            : new RemoteObjectReference(Handle) { SharedId = SharedId };
    }

    /// <summary>
    /// Converts this RemoteReference to a SharedReference.
    /// </summary>
    /// <returns>The SharedReference object representing this RemoteValue.</returns>
    public SharedReference ToSharedReference() => ToRemoteReference() is not SharedReference reference
        ? throw new BiDiException("Remote value cannot be converted to SharedReference")
        : reference;
}