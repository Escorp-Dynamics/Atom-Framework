using System.Runtime.CompilerServices;

namespace Atom.Net.Https.Http;

/// <summary>
/// 
/// </summary>
public readonly struct ConnectionIdLengths : IEquatable<ConnectionIdLengths>
{
    /// <summary>
    /// 
    /// </summary>
    public int SourceLength { get; init; }

    /// <summary>
    /// 
    /// </summary>
    public int DestinationLength { get; init; }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(SourceLength.GetHashCode(), DestinationLength.GetHashCode());

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ConnectionIdLengths other) => SourceLength.Equals(other.SourceLength) && DestinationLength.Equals(other.DestinationLength);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj switch
    {
        ConnectionIdLengths other => Equals(other),
        _ => default,
    };

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ConnectionIdLengths left, ConnectionIdLengths right) => left.Equals(right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ConnectionIdLengths left, ConnectionIdLengths right) => !(left == right);
}