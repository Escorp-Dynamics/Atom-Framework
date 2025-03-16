using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Atom.Buffers;

namespace Atom.Web.Browsing.BiDi.Session;

/// <summary>
/// Represents the capabilities requested for a session.
/// </summary>
public class CapabilitiesRequest : IPooled
{
    /// <summary>
    /// Gets or sets the set of capabilities that must be matched to create a new session.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    public CapabilityRequest? AlwaysMatch { get; set; }

    /// <summary>
    /// Gets or sets the list of sets of capabilities any of which may be matched to create a new session.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonInclude]
    public IEnumerable<CapabilityRequest>? FirstMatch { get; set; }

    /// <inheritdoc/>
    public void ClearForPool()
    {
        AlwaysMatch = default;
        FirstMatch = default;
    }

    /// <inheritdoc/>
    public static T Rent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>() where T : IPooled => ObjectPool<T>.Shared.Rent();

    /// <summary>
    /// Арендует экземпляр в пуле объектов.
    /// </summary>
    public static CapabilitiesRequest Rent() => Rent<CapabilitiesRequest>();

    /// <inheritdoc/>
    public static void Return<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(T value) where T : IPooled => ObjectPool<T>.Shared.Return(value, x => x.ClearForPool());
}