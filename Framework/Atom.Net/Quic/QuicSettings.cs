using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Atom.Net.Udp;

namespace Atom.Net.Quic;

/// <summary>
/// Представляет настройки QUIC-соединения.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="QuicSettings"/>.
/// </remarks>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
[StructLayout(LayoutKind.Auto)]
public readonly struct QuicSettings() : IEquatable<QuicSettings>
{
    /// <summary>
    /// Настройки UDP-уровня, применимые к используемому сокету (буферы, DSCP, TTL/HopLimit, pktinfo, и т.д.).
    /// </summary>
    /// <remarks>
    /// Если конструктор <see cref="QuicConnection"/> вызывается с параметром <see cref="UdpStream"/>,
    /// и это свойство не задано (<c>default</c>), будут использованы настройки из <c>udpStream.Settings</c>.
    /// </remarks>
    public UdpSettings Udp { get; init; }

    /// <summary>
    /// Максимальный полезный размер UDP-пакета (без IP/UDP заголовков).
    /// Типичные браузерные значения 1200..1350 для Initial, далее PMTU discovery.
    /// </summary>
    public int MaxUdpPayloadSize { get; init; } = 1252; // безопасный старт под Ethernet/IPv6

    /// <summary>
    /// Разрешить 0-RTT (при наличии PSK/SessionTicket). Влияет на отправку 0-RTT пакетов.
    /// </summary>
    public bool Enable0Rtt { get; init; } = false;

    /// <summary>
    /// Запретить миграцию пути (как делают браузеры при некоторых условиях).
    /// </summary>
    public bool DisableMigration { get; init; } = false;

    /// <summary>
    /// Включить GREASE значений transport parameters (по аналогии с браузерами).
    /// </summary>
    public bool EnableGrease { get; init; } = true;

    /// <summary>
    /// Начальный PTO (Probe Timeout) по RFC 9002. Типично 1с.
    /// </summary>
    public TimeSpan InitialPto { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Версия QUIC (по умолчанию v1).
    /// </summary>
    public QuicVersion Version { get; init; } = QuicVersion.V1;

    /// <summary>
    /// Отправлять PATH_CHALLENGE сразу.
    /// </summary>
    public bool ValidatePathOnStart { get; init; }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Udp.GetHashCode());
        hash.Add(MaxUdpPayloadSize.GetHashCode());
        hash.Add(Enable0Rtt.GetHashCode());
        hash.Add(DisableMigration.GetHashCode());
        hash.Add(EnableGrease.GetHashCode());
        hash.Add(InitialPto.GetHashCode());
        hash.Add(Version.GetHashCode());
        hash.Add(ValidatePathOnStart.GetHashCode());

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(QuicSettings other) => Udp.Equals(other.Udp) && MaxUdpPayloadSize.Equals(other.MaxUdpPayloadSize)
        && Enable0Rtt.Equals(other.Enable0Rtt) && DisableMigration.Equals(other.DisableMigration)
        && EnableGrease.Equals(other.EnableGrease) && InitialPto.Equals(other.InitialPto) && Version.Equals(other.Version)
        && ValidatePathOnStart.Equals(other.ValidatePathOnStart);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj switch
    {
        QuicSettings other => Equals(other),
        _ => default,
    };

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(QuicSettings left, QuicSettings right) => left.Equals(right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(QuicSettings left, QuicSettings right) => !(left == right);
}