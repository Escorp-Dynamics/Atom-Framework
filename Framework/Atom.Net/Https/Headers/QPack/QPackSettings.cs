using System.Runtime.CompilerServices;

namespace Atom.Net.Https.Headers.QPack;

/// <summary>
/// 
/// </summary>
public readonly struct QPackSettings : IEquatable<QPackSettings>
{
    /// <summary>
    /// 
    /// </summary>
    public bool HuffmanForNames { get; init; }     // браузеры — всегда true

    /// <summary>
    /// 
    /// </summary>
    public bool HuffmanForValues { get; init; }    // браузеры — всегда true

    /// <summary>
    /// 
    /// </summary>
    public bool IndexSensitiveHeaders { get; init; } // false для Cookie/Authorization и т.п.

    /// <summary>
    /// 
    /// </summary>
    public bool DynamicTableAutoTune { get; init; }  // Chrome может динамически подстраивать

    /// <summary>
    /// 
    /// </summary>
    public uint MaxHeaderListSize { get; init; }     // SETTINGS_MAX_HEADER_LIST_SIZE

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HashCode.Combine(
        HuffmanForNames.GetHashCode(),
        HuffmanForValues.GetHashCode(),
        IndexSensitiveHeaders.GetHashCode(),
        DynamicTableAutoTune.GetHashCode(),
        MaxHeaderListSize.GetHashCode()
    );

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(QPackSettings other) => HuffmanForNames.Equals(other.HuffmanForNames) && HuffmanForValues.Equals(other.HuffmanForValues)
        && IndexSensitiveHeaders.Equals(other.IndexSensitiveHeaders) && DynamicTableAutoTune.Equals(other.DynamicTableAutoTune)
        && MaxHeaderListSize.Equals(other.MaxHeaderListSize);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj switch
    {
        QPackSettings other => Equals(other),
        _ => default,
    };

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(QPackSettings left, QPackSettings right) => left.Equals(right);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(QPackSettings left, QPackSettings right) => !(left == right);
}