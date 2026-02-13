#pragma warning disable CA1027, CA1028

namespace Atom.Media;

/// <summary>
/// Формат аудио семплов.
/// </summary>
public enum AudioSampleFormat : byte
{
    /// <summary>Неизвестный формат.</summary>
    Unknown = 0,

    // ═══════════════════════════════════════════════════════════════
    // INTERLEAVED (чередующиеся каналы: L R L R L R ...)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Unsigned 8-bit, interleaved.</summary>
    U8 = 1,

    /// <summary>Signed 16-bit, little-endian, interleaved.</summary>
    S16 = 2,

    /// <summary>Signed 32-bit, little-endian, interleaved.</summary>
    S32 = 3,

    /// <summary>Float 32-bit, little-endian, interleaved.</summary>
    F32 = 4,

    /// <summary>Float 64-bit, little-endian, interleaved.</summary>
    F64 = 5,

    // ═══════════════════════════════════════════════════════════════
    // PLANAR (раздельные плоскости: LLLL... RRRR...)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Unsigned 8-bit, planar.</summary>
    U8Planar = 16,

    /// <summary>Signed 16-bit, planar.</summary>
    S16Planar = 17,

    /// <summary>Signed 32-bit, planar.</summary>
    S32Planar = 18,

    /// <summary>Float 32-bit, planar.</summary>
    F32Planar = 19,

    /// <summary>Float 64-bit, planar.</summary>
    F64Planar = 20,
}

/// <summary>
/// Расширения для <see cref="AudioSampleFormat"/>.
/// </summary>
public static class AudioSampleFormatExtensions
{
    /// <summary>
    /// Возвращает размер одного семпла в байтах.
    /// </summary>
    public static int GetBytesPerSample(this AudioSampleFormat format) => format switch
    {
        AudioSampleFormat.U8 or AudioSampleFormat.U8Planar => 1,
        AudioSampleFormat.S16 or AudioSampleFormat.S16Planar => 2,
        AudioSampleFormat.S32 or AudioSampleFormat.S32Planar or
        AudioSampleFormat.F32 or AudioSampleFormat.F32Planar => 4,
        AudioSampleFormat.F64 or AudioSampleFormat.F64Planar => 8,
        _ => 0,
    };

    /// <summary>
    /// Возвращает true, если формат planar (раздельные каналы).
    /// </summary>
    public static bool IsPlanar(this AudioSampleFormat format) => format is
        AudioSampleFormat.U8Planar or AudioSampleFormat.S16Planar or
        AudioSampleFormat.S32Planar or AudioSampleFormat.F32Planar or
        AudioSampleFormat.F64Planar;

    /// <summary>
    /// Возвращает true, если формат использует числа с плавающей точкой.
    /// </summary>
    public static bool IsFloat(this AudioSampleFormat format) => format is
        AudioSampleFormat.F32 or AudioSampleFormat.F64 or
        AudioSampleFormat.F32Planar or AudioSampleFormat.F64Planar;

    /// <summary>
    /// Вычисляет размер буфера для аудио в байтах.
    /// </summary>
    public static int CalculateBufferSize(this AudioSampleFormat format, int sampleCount, int channels)
        => sampleCount * channels * format.GetBytesPerSample();
}
