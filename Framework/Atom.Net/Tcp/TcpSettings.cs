using System.Net;
using System.Runtime.CompilerServices;

namespace Atom.Net.Tcp;

/// <summary>
/// Представляет настройки TCP-соединения.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="TcpSettings"/>.
/// </remarks>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly struct TcpSettings() : IEquatable<TcpSettings>
{
    /// <summary>
    /// Включить ли NoDelay (отключение Nagle).
    /// </summary>
    public bool IsNagleDisabled { get; init; }

    /// <summary>
    /// Размер буфера отправки.
    /// </summary>
    public int SendBufferSize { get; init; }

    /// <summary>
    /// Размер буфера приёма.
    /// </summary>
    public int ReceiveBufferSize { get; init; }

    /// <summary>
    /// TFO client.
    /// </summary>
    public bool UseFastOpen { get; init; }

    /// <summary>
    /// TTL / HopLimit.
    /// </summary>
    public byte TimeToLive { get; init; }

    /// <summary>
    /// MSS clamp.
    /// </summary>
    public int MaxSegmentSize { get; init; }

    /// <summary>
    /// Адресная гонка.
    /// </summary>
    public bool UseHappyEyeballsAlternating { get; init; } = true;

    /// <summary>
    /// 50–250 мс.
    /// </summary>
    public TimeSpan HappyEyeballsDelay { get; init; }

    /// <summary>
    /// Интервал между последующими попытками (обычно ~250 мс).
    /// </summary>
    public TimeSpan HappyEyeballsStepDelay { get; init; }

    /// <summary>
    /// Максимум одновременных попыток подключения (обычно 2–3).
    /// </summary>
    public int HappyEyeballsMaxConcurrency { get; init; }

    /// <summary>
    /// Локальная конечная точка для клиентского Bind.
    /// Рекомендуется указывать порт 0. Для IPv6 link-local задавайте корректный ScopeId на <see cref="IPAddress"/>.
    /// </summary>
    public IPEndPoint? LocalEndPoint { get; init; }

    /// <summary>
    /// DSCP/ECN.
    /// </summary>
    public byte Dscp { get; init; }

    /// <summary>
    /// Возвращает или задает задержку проверки на активность.
    /// </summary>
    public TimeSpan KeepAlivePingDelay { get; init; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Возвращает или задает время ожидания проверки на активность.
    /// </summary>
    public TimeSpan KeepAlivePingTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Возвращает или задает поведение проверки на активность.
    /// </summary>
    public HttpKeepAlivePingPolicy KeepAlivePingPolicy { get; init; }

    /// <summary>
    /// Пауза после установления TCP перед началом TLS.
    /// </summary>
    public TimeSpan Delay { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Таймаут одной попытки Connect (в т.ч. в HEv2), по умолчанию 3 секунды.
    /// Значение &lt;= 0 отключает пер-попыточный таймаут.
    /// </summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode()
    {
        var hashCode = new HashCode();

        hashCode.Add(IsNagleDisabled.GetHashCode());
        hashCode.Add(SendBufferSize.GetHashCode());
        hashCode.Add(ReceiveBufferSize.GetHashCode());
        hashCode.Add(UseFastOpen.GetHashCode());
        hashCode.Add(TimeToLive.GetHashCode());
        hashCode.Add(MaxSegmentSize.GetHashCode());
        hashCode.Add(UseHappyEyeballsAlternating.GetHashCode());
        hashCode.Add(HappyEyeballsDelay.GetHashCode());
        hashCode.Add(HappyEyeballsStepDelay.GetHashCode());
        hashCode.Add(HappyEyeballsMaxConcurrency.GetHashCode());

        if (LocalEndPoint is not null) hashCode.Add(LocalEndPoint.GetHashCode());

        hashCode.Add(Dscp.GetHashCode());
        hashCode.Add(KeepAlivePingDelay.GetHashCode());
        hashCode.Add(KeepAlivePingTimeout.GetHashCode());
        hashCode.Add(KeepAlivePingPolicy.GetHashCode());
        hashCode.Add(Delay.GetHashCode());
        hashCode.Add(AttemptTimeout.GetHashCode());

        return hashCode.ToHashCode();
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(TcpSettings other) => IsNagleDisabled.Equals(other.IsNagleDisabled) && SendBufferSize.Equals(other.SendBufferSize)
        && ReceiveBufferSize.Equals(other.ReceiveBufferSize) && UseFastOpen.Equals(other.UseFastOpen)
        && TimeToLive.Equals(other.TimeToLive) && MaxSegmentSize.Equals(other.MaxSegmentSize)
        && UseHappyEyeballsAlternating.Equals(other.UseHappyEyeballsAlternating) && HappyEyeballsDelay.Equals(other.HappyEyeballsDelay)
        && HappyEyeballsStepDelay.Equals(other.HappyEyeballsStepDelay) && HappyEyeballsMaxConcurrency.Equals(other.HappyEyeballsMaxConcurrency)
        && ((LocalEndPoint is null && other.LocalEndPoint is null) || (LocalEndPoint is not null && other.LocalEndPoint is not null && LocalEndPoint.Equals(other.LocalEndPoint)))
        && Dscp.Equals(other.Dscp)
        && KeepAlivePingDelay.Equals(other.KeepAlivePingDelay) && KeepAlivePingTimeout.Equals(other.KeepAlivePingTimeout)
        && KeepAlivePingPolicy.Equals(other.KeepAlivePingPolicy) && Delay.Equals(other.Delay)
        && AttemptTimeout.Equals(other.AttemptTimeout);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj switch
    {
        TcpSettings other => Equals(other),
        _ => default,
    };

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(TcpSettings left, TcpSettings right) => left.Equals(right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(TcpSettings left, TcpSettings right) => !(left == right);
}