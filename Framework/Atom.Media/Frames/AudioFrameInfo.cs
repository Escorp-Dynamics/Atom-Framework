using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media;

/// <summary>
/// Метаданные аудиокадра.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct AudioFrameInfo
{
    /// <summary>Количество семплов на канал.</summary>
    public required int SampleCount { get; init; }

    /// <summary>Количество каналов.</summary>
    public required int ChannelCount { get; init; }

    /// <summary>Частота дискретизации (Hz).</summary>
    public required int SampleRate { get; init; }

    /// <summary>Формат семплов.</summary>
    public required AudioSampleFormat SampleFormat { get; init; }

    /// <summary>Presentation timestamp в микросекундах.</summary>
    public long PtsUs { get; init; }

    /// <summary>Duration в микросекундах.</summary>
    public long DurationUs { get; init; }

    /// <summary>
    /// Возвращает presentation timestamp как TimeSpan.
    /// </summary>
    public TimeSpan Pts
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => TimeSpan.FromMicroseconds(PtsUs);
    }

    /// <summary>
    /// Возвращает duration как TimeSpan.
    /// </summary>
    public TimeSpan Duration
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => TimeSpan.FromMicroseconds(DurationUs);
    }

    /// <summary>
    /// Вычисляет размер буфера для данного формата.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int CalculateBufferSize() => SampleFormat.CalculateBufferSize(SampleCount, ChannelCount);

    /// <summary>
    /// Вычисляет duration в микросекундах по количеству семплов.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long CalculateDurationUs() => SampleRate > 0 ? SampleCount * 1_000_000L / SampleRate : 0;
}
