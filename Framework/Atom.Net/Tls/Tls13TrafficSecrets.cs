using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Net.Tls;

/// <summary>
/// Набор трафиковых секретов TLS 1.3.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="Tls13TrafficSecrets"/>.
/// </remarks>
/// <param name="ch"></param>
/// <param name="sh"></param>
/// <param name="ca"></param>
/// <param name="sa"></param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
[StructLayout(LayoutKind.Auto)]
public readonly struct Tls13TrafficSecrets(ReadOnlyMemory<byte> ch, ReadOnlyMemory<byte> sh, ReadOnlyMemory<byte> ca, ReadOnlyMemory<byte> sa) : IEquatable<Tls13TrafficSecrets>
{
    /// <summary>
    /// 
    /// </summary>
    public readonly ReadOnlyMemory<byte> ClientHandshake { get; } = ch;

    /// <summary>
    /// 
    /// </summary>
    public readonly ReadOnlyMemory<byte> ServerHandshake { get; } = sh;

    /// <summary>
    /// 
    /// </summary>
    public readonly ReadOnlyMemory<byte> ClientApp { get; } = ca;

    /// <summary>
    /// 
    /// </summary>
    public readonly ReadOnlyMemory<byte> ServerApp { get; } = sa;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(ClientHandshake, ServerHandshake, ClientApp, ServerApp);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Tls13TrafficSecrets other) => ClientHandshake.Equals(other.ClientHandshake) && ServerHandshake.Equals(other.ServerHandshake)
        && ClientApp.Equals(other.ClientApp) && ServerApp.Equals(other.ServerApp);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj switch
    {
        Tls13TrafficSecrets other => Equals(other),
        _ => default,
    };

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Tls13TrafficSecrets left, Tls13TrafficSecrets right) => left.Equals(right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Tls13TrafficSecrets left, Tls13TrafficSecrets right) => !(left == right);
}