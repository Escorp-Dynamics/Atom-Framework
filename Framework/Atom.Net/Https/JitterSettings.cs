using System.Runtime.CompilerServices;

namespace Atom.Net.Https;

/// <summary>
/// 
/// </summary>
[Flags]
public enum JitterPhases : byte
{
    /// <summary>
    /// 
    /// </summary>
    None = 0,
    /// <summary>
    /// 
    /// </summary>
    TcpConnect = 1,
    /// <summary>
    /// 
    /// </summary>
    TlsHandshake = 2,
    /// <summary>
    /// 
    /// </summary>
    HttpPreface = 4,
    /// <summary>
    /// 
    /// </summary>
    HeadersEmit = 8,
    /// <summary>
    /// 
    /// </summary>
    BetweenHeaders = 16,
    /// <summary>
    /// 
    /// </summary>
    BodyChunks = 32,
}

/// <summary>
/// 
/// </summary>
public enum JitterDistribution : byte
{
    /// <summary>
    /// 
    /// </summary>
    Uniform,
    /// <summary>
    /// 
    /// </summary>
    Normal,
    /// <summary>
    /// 
    /// </summary>
    LogNormal,
}

/// <summary>
/// Представляет настройки эмуляции джиттера браузера.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="JitterSettings"/>.
/// </remarks>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly struct JitterSettings() : IEquatable<JitterSettings>
{
    /// <summary>
    /// Минимальная задержка.
    /// </summary>
    public TimeSpan Min { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Максимальная задержка.
    /// </summary>
    public TimeSpan Max { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// 
    /// </summary>
    public JitterDistribution Distribution { get; init; }

    /// <summary>
    /// 
    /// </summary>
    public JitterPhases Phases { get; init; }

    /// <summary>
    /// 
    /// </summary>
    public uint Seed { get; init; }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine
    (
        Min.GetHashCode(),
        Max.GetHashCode(),
        Distribution.GetHashCode(),
        Phases.GetHashCode(),
        Seed.GetHashCode()
    );

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(JitterSettings other) => Min.Equals(other.Min) && Max.Equals(other.Max)
        && Distribution.Equals(other.Distribution) && Phases.Equals(other.Phases) && Seed.Equals(other.Seed);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj switch
    {
        JitterSettings other => Equals(other),
        _ => default,
    };

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(JitterSettings left, JitterSettings right) => left.Equals(right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(JitterSettings left, JitterSettings right) => !(left == right);
}