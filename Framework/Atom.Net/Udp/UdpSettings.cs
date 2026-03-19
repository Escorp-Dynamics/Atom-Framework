using System.Net;
using System.Runtime.CompilerServices;

namespace Atom.Net.Udp;

/// <summary>
/// Настройки клиентского UDP (для QUIC и пр.).
/// </summary>
/// <remarks>Значения по умолчанию безопасны и близки к типичным браузерным.</remarks>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly struct UdpSettings() : IEquatable<UdpSettings>
{
    /// <summary>
    /// Размер буфера отправки. 0 — оставить по умолчанию ОС.
    /// </summary>
    public int SendBufferSize { get; init; }

    /// <summary>
    /// Размер буфера приёма. 0 — оставить по умолчанию ОС.
    /// </summary>
    public int ReceiveBufferSize { get; init; }

    /// <summary>
    /// TTL/HopLimit. 0 — не устанавливать (оставить ОС).
    /// </summary>
    public byte TimeToLive { get; init; }

    /// <summary>
    /// DSCP для IPv4 ToS или IPv6 Traffic Class. 0 — не устанавливать.
    /// </summary>
    public byte Dscp { get; init; }

    /// <summary>
    /// Запрет IPv4-фрагментации (PMTUD). Применяется там, где доступно.
    /// </summary>
    public bool DontFragment { get; init; }

    /// <summary>
    /// Локальная конечная точка для клиентского Bind.
    /// Рекомендуется указывать порт 0. Для IPv6 link-local задавайте корректный ScopeId на <see cref="IPAddress"/>.
    /// </summary>
    public IPEndPoint? LocalEndPoint { get; init; }

    /// <summary>
    /// Включить Happy Eyeballs v2 (чередование IPv6/IPv4).
    /// </summary>
    public bool UseHappyEyeballsAlternating { get; init; } = true;

    /// <summary>
    /// Начальная задержка перед запуском альтернативного семейства (обычно ~200 мс).
    /// </summary>
    public TimeSpan HappyEyeballsDelay { get; init; }

    /// <summary>
    /// Интервал между последующими «ступенями» HEv2 (обычно ~250 мс).
    /// </summary>
    public TimeSpan HappyEyeballsStepDelay { get; init; }

    /// <summary>
    /// Максимум конкурентных адресных попыток (2–3 достаточно для клиента).
    /// </summary>
    public int HappyEyeballsMaxConcurrency { get; init; }

    /// <summary>
    /// Пер-попыточный таймаут (Connect + опциональные задержки). ≤0 — выкл. Рекомендация: 2–3с.
    /// </summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Подавлять Windows SIO_UDP_CONNRESET (ICMP Port Unreachable → исключение). Рекомендуется <see langword="true"/>.
    /// </summary>
    public bool UseConnectionResetWorkaround { get; init; } = true;

    /// <summary>
    /// Включить приём packet information (pktinfo), позволяющий получать адрес назначения пакета.
    /// Полезно для Path Validation / Connection Migration (мимикрия браузеров).
    /// </summary>
    public bool UsePacketInfo { get; init; }

    /// <summary>
    /// Включить Explicit Congestion Notification (ECN) для UDP сокета, если поддерживается ОС.
    /// </summary>
    public bool UseEcn { get; init; }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode()
    {
        var hashCode = new HashCode();

        hashCode.Add(SendBufferSize.GetHashCode());
        hashCode.Add(ReceiveBufferSize.GetHashCode());
        hashCode.Add(TimeToLive.GetHashCode());
        hashCode.Add(Dscp.GetHashCode());
        hashCode.Add(DontFragment.GetHashCode());

        if (LocalEndPoint is not null) hashCode.Add(LocalEndPoint.GetHashCode());

        hashCode.Add(UseHappyEyeballsAlternating.GetHashCode());
        hashCode.Add(HappyEyeballsDelay.GetHashCode());
        hashCode.Add(HappyEyeballsStepDelay.GetHashCode());
        hashCode.Add(HappyEyeballsMaxConcurrency.GetHashCode());
        hashCode.Add(AttemptTimeout.GetHashCode());
        hashCode.Add(UseConnectionResetWorkaround.GetHashCode());
        hashCode.Add(UsePacketInfo.GetHashCode());
        hashCode.Add(UseEcn.GetHashCode());

        return hashCode.ToHashCode();
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(UdpSettings other) => SendBufferSize.Equals(other.SendBufferSize) && ReceiveBufferSize.Equals(other.ReceiveBufferSize)
        && TimeToLive.Equals(other.TimeToLive) && Dscp.Equals(other.Dscp)
        && DontFragment.Equals(other.DontFragment) && ((LocalEndPoint is null && other.LocalEndPoint is null) || (LocalEndPoint is not null && other.LocalEndPoint is not null && LocalEndPoint.Equals(other.LocalEndPoint)))
        && UseHappyEyeballsAlternating.Equals(other.UseHappyEyeballsAlternating) && HappyEyeballsDelay.Equals(other.HappyEyeballsDelay)
        && HappyEyeballsStepDelay.Equals(other.HappyEyeballsStepDelay) && HappyEyeballsMaxConcurrency.Equals(other.HappyEyeballsMaxConcurrency)
        && AttemptTimeout.Equals(other.AttemptTimeout) && UseConnectionResetWorkaround.Equals(other.UseConnectionResetWorkaround)
        && UsePacketInfo.Equals(other.UsePacketInfo) && UseEcn.Equals(other.UseEcn);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj switch
    {
        UdpSettings other => Equals(other),
        _ => default,
    };

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(UdpSettings left, UdpSettings right) => left.Equals(right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(UdpSettings left, UdpSettings right) => !(left == right);
}