using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Atom.Net.Https;

/// <summary>
/// Представляет данные JA3.
/// </summary>
public readonly struct Ja3 : IEquatable<Ja3>
{
    /// <summary>
    /// Исходное значение.
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// MD5-хэш.
    /// </summary>
    public string Hash
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
#pragma warning disable CA5351
            var hash = MD5.HashData(Encoding.ASCII.GetBytes(Value));
#pragma warning restore CA5351
            return Convert.ToHexStringLower(hash);
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Ja3 other) => GetHashCode() == other.GetHashCode();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj switch
    {
        Ja3 other => Equals(other),
        _ => default,
    };

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Ja3 left, Ja3 right) => left.Equals(right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Ja3 left, Ja3 right) => !(left == right);
}