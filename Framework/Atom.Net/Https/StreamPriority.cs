using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Net.Https.Http;

/// <summary>
/// Представляет данные о приоритете потока.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct StreamPriority : IEquatable<StreamPriority>
{
    /// <summary>
    /// Идентификатор потока.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// 
    /// </summary>
    public int DependsOn { get; init; }

    /// <summary>
    /// Вес потока.
    /// </summary>
    public byte Weight { get; init; }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(Id.GetHashCode(), DependsOn.GetHashCode(), Weight.GetHashCode());

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(StreamPriority other) => Id.Equals(other.Id) && DependsOn.Equals(other.DependsOn) && Weight.Equals(other.Weight);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj switch
    {
        StreamPriority other => Equals(other),
        _ => default,
    };

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(StreamPriority left, StreamPriority right) => left.Equals(right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(StreamPriority left, StreamPriority right) => !(left == right);
}