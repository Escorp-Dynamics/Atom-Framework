using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Структура идентификатора PSK для TLS расширения pre_shared_key
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PskIdentity : IEquatable<PskIdentity>
{
    /// <summary>
    /// 
    /// </summary>
    public ReadOnlyMemory<byte> Identity { get; init; }

    /// <summary>
    /// 
    /// </summary>
    public uint ObfuscatedTicketAge { get; init; }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(Identity.GetHashCode(), ObfuscatedTicketAge.GetHashCode());

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(PskIdentity other) => GetHashCode() == other.GetHashCode();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj switch
    {
        PskIdentity other => Equals(other),
        _ => default,
    };

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(PskIdentity left, PskIdentity right) => left.Equals(right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(PskIdentity left, PskIdentity right) => !(left == right);
}