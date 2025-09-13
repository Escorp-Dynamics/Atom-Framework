using System.Net;
using System.Runtime.CompilerServices;

namespace Atom.Net.Udp;

/// <summary>
/// представляет сведения pktinfo о полученной дейтаграмме (локальный адрес назначения и интерфейс).
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="UdpPacketInfo"/>.
/// </remarks>
/// <param name="localAddress">Локальный адрес, на который пришёл пакет.</param>
/// <param name="ifIndex">Индекс сетевого интерфейса.</param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly struct UdpPacketInfo(IPAddress? localAddress, int ifIndex) : IEquatable<UdpPacketInfo>
{
    /// <summary>
    /// Локальный адрес, на который пришёл пакет (а не wildcard из Bind).
    /// </summary>
    public IPAddress? LocalAddress { get; } = localAddress;

    /// <summary>
    /// Индекс сетевого интерфейса (если доступен; иначе 0).
    /// </summary>
    public int InterfaceIndex { get; } = ifIndex;

    /// <summary>
    /// Признак валидности данных pktinfo.
    /// </summary>
    public bool HasValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => LocalAddress is not null;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(LocalAddress?.GetHashCode(), InterfaceIndex.GetHashCode());

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(UdpPacketInfo other) => ((LocalAddress is null && other.LocalAddress is null) || (LocalAddress is not null && other.LocalAddress is not null && LocalAddress.Equals(other.LocalAddress))) && InterfaceIndex.Equals(other.InterfaceIndex);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj switch
    {
        UdpPacketInfo other => Equals(other),
        _ => default,
    };

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(UdpPacketInfo left, UdpPacketInfo right) => left.Equals(right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(UdpPacketInfo left, UdpPacketInfo right) => !(left == right);
}