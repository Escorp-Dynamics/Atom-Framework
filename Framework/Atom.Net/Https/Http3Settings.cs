using System.Runtime.CompilerServices;
using Atom.Net.Https.Headers;
using Atom.Net.Https.Headers.QPack;
using Atom.Net.Quic;
using Atom.Net.Tls;

namespace Atom.Net.Https.Http;

/// <summary>
/// Представляет настройки HTTP/3.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="Http3Settings"/>.
/// </remarks>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly struct Http3Settings() : IEquatable<Http3Settings>
{
    /// <summary>
    /// Политика форматирования заголовков.
    /// </summary>
    public IHeadersFormattingPolicy? HeadersFormattingPolicy { get; init; }

    /// <summary>
    /// 
    /// </summary>
    public uint MaxData { get; init; } = 1048576;

    /// <summary>
    /// 
    /// </summary>
    public uint MaxStreamDataBidiLocal { get; init; } = 262144;

    /// <summary>
    /// 
    /// </summary>
    public uint MaxStreamDataBidiRemote { get; init; } = 262144;

    /// <summary>
    /// 
    /// </summary>
    public uint MaxStreamsBidi { get; init; } = 100;

    /// <summary>
    /// 
    /// </summary>
    public uint MaxStreamsUni { get; init; } = 3;

    /// <summary>
    /// initial_max_stream_data_uni.
    /// </summary>
    public uint MaxStreamDataUni { get; init; }

    /// <summary>
    /// 
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 
    /// </summary>
    public byte AckDelayExponent { get; init; } = 3;

    /// <summary>
    /// transport parameter.
    /// </summary>
    public TimeSpan MaxAckDelay { get; init; }

    /// <summary>
    /// 
    /// </summary>
    public byte ActiveConnectionIdLimit { get; init; } = 2;

    /// <summary>
    /// 
    /// </summary>
    public ushort MaxUdpPayloadSize { get; init; } = 1350;

    /// <summary>
    /// Добавлять зарезервированные H3 settings.
    /// </summary>
    public bool UseGreaseOnSettings { get; init; }

    /// <summary>
    /// 
    /// </summary>
    public bool UseGreaseOnQuicVersions { get; init; } = true;

    /// <summary>
    /// 
    /// </summary>
    public bool UseSpinBit { get; init; } = true;

    /// <summary>
    /// 
    /// </summary>
    public ConnectionIdLengths ConnectionIdLengths { get; init; }

    /// <summary>
    /// 
    /// </summary>
    public bool Use0Rtt { get; init; }

    /// <summary>
    /// 
    /// </summary>
    public bool UseDatagrams { get; init; }

    /// <summary>
    /// Приоритизация.
    /// </summary>
    public bool UsePriorityUpdate { get; init; }

    /// <summary>
    /// 
    /// </summary>
    public int QpackMaxTableCapacity { get; init; } = 65536;

    /// <summary>
    /// 
    /// </summary>
    public int QpackBlockedStreams { get; init; } = 16;

    /// <summary>
    /// Возвращает или задает значение, указывающее, можно ли установить дополнительные подключения HTTP/3 к тому же серверу.
    /// </summary>
    public bool UseMultipleConnections { get; init; }

    /// <summary>
    /// Настройки TLS.
    /// </summary>
    public TlsSettings Tls { get; init; }

    /// <summary>
    /// Настройки QUIC.
    /// </summary>
    public QuicSettings Quic { get; init; }

    /// <summary>
    /// Настройки QPack.
    /// </summary>
    public QPackSettings QPack { get; init; }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(HeadersFormattingPolicy?.GetHashCode());
        hash.Add(MaxData.GetHashCode());
        hash.Add(MaxStreamDataBidiLocal.GetHashCode());
        hash.Add(MaxStreamDataBidiRemote.GetHashCode());
        hash.Add(MaxStreamsBidi.GetHashCode());
        hash.Add(MaxStreamsUni.GetHashCode());
        hash.Add(MaxStreamDataUni.GetHashCode());
        hash.Add(IdleTimeout.GetHashCode());
        hash.Add(AckDelayExponent.GetHashCode());
        hash.Add(MaxAckDelay.GetHashCode());
        hash.Add(ActiveConnectionIdLimit.GetHashCode());
        hash.Add(MaxUdpPayloadSize.GetHashCode());
        hash.Add(UseGreaseOnQuicVersions.GetHashCode());
        hash.Add(UseGreaseOnSettings.GetHashCode());
        hash.Add(UseSpinBit.GetHashCode());
        hash.Add(ConnectionIdLengths.GetHashCode());
        hash.Add(Use0Rtt.GetHashCode());
        hash.Add(UseDatagrams.GetHashCode());
        hash.Add(UsePriorityUpdate.GetHashCode());
        hash.Add(QpackMaxTableCapacity.GetHashCode());
        hash.Add(QpackBlockedStreams.GetHashCode());
        hash.Add(UseMultipleConnections.GetHashCode());
        hash.Add(Tls.GetHashCode());
        hash.Add(Quic.GetHashCode());
        hash.Add(QPack.GetHashCode());

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Http3Settings other) => ((HeadersFormattingPolicy is null && other.HeadersFormattingPolicy is null) || (HeadersFormattingPolicy is not null && other.HeadersFormattingPolicy is not null && HeadersFormattingPolicy.Equals(other.HeadersFormattingPolicy))) && MaxData.Equals(other.MaxData) && MaxStreamDataBidiLocal.Equals(other.MaxStreamDataBidiLocal)
        && MaxStreamDataBidiRemote.Equals(other.MaxStreamDataBidiRemote) && MaxStreamsBidi.Equals(other.MaxStreamsBidi)
        && MaxStreamsUni.Equals(other.MaxStreamsUni) && IdleTimeout.Equals(other.IdleTimeout)
        && AckDelayExponent.Equals(other.AckDelayExponent) && ActiveConnectionIdLimit.Equals(other.ActiveConnectionIdLimit)
        && MaxUdpPayloadSize.Equals(other.MaxUdpPayloadSize) && UseGreaseOnQuicVersions.Equals(other.UseGreaseOnQuicVersions)
        && UseSpinBit.Equals(other.UseSpinBit) && ConnectionIdLengths.Equals(other.ConnectionIdLengths)
        && Use0Rtt.Equals(other.Use0Rtt) && UseDatagrams.Equals(other.UseDatagrams)
        && UsePriorityUpdate.Equals(other.UsePriorityUpdate) && QpackMaxTableCapacity.Equals(other.QpackMaxTableCapacity)
        && QpackBlockedStreams.Equals(other.QpackBlockedStreams) && UseMultipleConnections.Equals(other.UseMultipleConnections)
        && Tls.Equals(other.Tls) && Quic.Equals(other.Quic) && MaxStreamDataUni.Equals(other.MaxStreamDataUni)
        && MaxAckDelay.Equals(other.MaxAckDelay) && UseGreaseOnSettings.Equals(other.UseGreaseOnSettings)
        && QPack.Equals(other.QPack);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj switch
    {
        Http3Settings other => Equals(other),
        _ => default,
    };

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Http3Settings left, Http3Settings right) => left.Equals(right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Http3Settings left, Http3Settings right) => !(left == right);
}