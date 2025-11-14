using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Net.Https.Connections;

/// <summary>
/// Представляет SETTINGS для установки порядка.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct ConnectionSettings : IEquatable<ConnectionSettings>
{
    /// <summary>
    /// Идентификатор SETTINGS.
    /// </summary>
    public uint Id { get; init; }

    /// <summary>
    /// Значение.
    /// </summary>
    public uint Value { get; init; }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(Id.GetHashCode(), Value.GetHashCode());

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ConnectionSettings other) => Id.Equals(other.Id) && Value.Equals(other.Value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj switch
    {
        ConnectionSettings other => Equals(other),
        _ => default,
    };

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ConnectionSettings left, ConnectionSettings right) => left.Equals(right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ConnectionSettings left, ConnectionSettings right) => !(left == right);
}