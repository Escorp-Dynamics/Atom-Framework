using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Media;

/// <summary>
/// Информация о медиа потоке в контейнере.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct MediaStreamInfo
{
    /// <summary>Индекс потока в контейнере.</summary>
    public required int Index { get; init; }

    /// <summary>Тип потока.</summary>
    public required MediaStreamType Type { get; init; }

    /// <summary>ID кодека.</summary>
    public required MediaCodecId CodecId { get; init; }

    /// <summary>Длительность в микросекундах (-1 если неизвестна).</summary>
    public long DurationUs { get; init; }

    /// <summary>Битрейт в bps (0 если неизвестен).</summary>
    public long BitRate { get; init; }

    /// <summary>Дополнительные данные кодека (SPS/PPS, AudioSpecificConfig и т.д.).</summary>
    public ReadOnlyMemory<byte> ExtraData { get; init; }

    /// <summary>Параметры видео (только для Video).</summary>
    public VideoCodecParameters? VideoParameters { get; init; }

    /// <summary>Параметры аудио (только для Audio).</summary>
    public AudioCodecParameters? AudioParameters { get; init; }

    /// <summary>
    /// Длительность как TimeSpan.
    /// </summary>
    public TimeSpan Duration
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => DurationUs >= 0 ? TimeSpan.FromMicroseconds(DurationUs) : TimeSpan.Zero;
    }
}
