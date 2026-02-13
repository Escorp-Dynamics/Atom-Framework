using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media;

/// <summary>
/// Представляет закодированный пакет данных.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly ref struct MediaPacket
{
    /// <summary>
    /// Данные пакета.
    /// </summary>
    public readonly ReadOnlySpan<byte> Data;

    /// <summary>
    /// Индекс потока.
    /// </summary>
    public readonly int StreamIndex;

    /// <summary>
    /// Presentation timestamp в микросекундах.
    /// </summary>
    public readonly long PtsUs;

    /// <summary>
    /// Decode timestamp в микросекундах.
    /// </summary>
    public readonly long DtsUs;

    /// <summary>
    /// Длительность в микросекундах.
    /// </summary>
    public readonly long DurationUs;

    /// <summary>
    /// Свойства пакета.
    /// </summary>
    public readonly PacketProperty Properties;

    /// <summary>
    /// Возвращает true, если это ключевой кадр.
    /// </summary>
    public bool IsKeyframe
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Properties & PacketProperty.Keyframe) != PacketProperty.None;
    }

    /// <summary>
    /// Возвращает true, если пакет пуст.
    /// </summary>
    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Data.IsEmpty;
    }

    /// <summary>
    /// Presentation timestamp как TimeSpan.
    /// </summary>
    public TimeSpan Pts
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => TimeSpan.FromMicroseconds(PtsUs);
    }

    /// <summary>
    /// Decode timestamp как TimeSpan.
    /// </summary>
    public TimeSpan Dts
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => TimeSpan.FromMicroseconds(DtsUs);
    }

    /// <summary>
    /// Duration как TimeSpan.
    /// </summary>
    public TimeSpan Duration
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => TimeSpan.FromMicroseconds(DurationUs);
    }

    /// <summary>
    /// Создаёт пакет.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MediaPacket(
        ReadOnlySpan<byte> data,
        int streamIndex,
        long ptsUs,
        long dtsUs,
        long durationUs,
        PacketProperty properties = PacketProperty.None)
    {
        Data = data;
        StreamIndex = streamIndex;
        PtsUs = ptsUs;
        DtsUs = dtsUs;
        DurationUs = durationUs;
        Properties = properties;
    }

    /// <summary>
    /// Создаёт пакет с TimeSpan.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MediaPacket(
        ReadOnlySpan<byte> data,
        int streamIndex,
        TimeSpan pts,
        TimeSpan dts,
        TimeSpan duration,
        PacketProperty properties = PacketProperty.None)
    {
        Data = data;
        StreamIndex = streamIndex;
        PtsUs = (long)pts.TotalMicroseconds;
        DtsUs = (long)dts.TotalMicroseconds;
        DurationUs = (long)duration.TotalMicroseconds;
        Properties = properties;
    }
}
