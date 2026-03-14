#pragma warning disable CA1308

using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Atom.Media.Audio;

/// <summary>
/// Конвертирует аудио данные между различными <see cref="AudioSampleFormat"/>.
/// Поддерживает конвертацию между всеми interleaved и planar форматами,
/// а также deinterleave/interleave операции.
/// </summary>
public static class AudioResampler
{
    /// <summary>
    /// Конвертирует аудио данные из одного формата в другой.
    /// </summary>
    /// <param name="source">Исходные аудио данные.</param>
    /// <param name="sourceFormat">Формат исходных данных.</param>
    /// <param name="destinationFormat">Целевой формат.</param>
    /// <param name="channels">Количество аудиоканалов.</param>
    /// <returns>Конвертированные аудио данные.</returns>
    public static byte[] Convert(
        ReadOnlySpan<byte> source,
        AudioSampleFormat sourceFormat,
        AudioSampleFormat destinationFormat,
        int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);

        if (sourceFormat == destinationFormat)
        {
            return source.ToArray();
        }

        var sampleCount = CountSamples(source, sourceFormat);

        var outputSize = destinationFormat.CalculateBufferSize(
            sampleCount / channels, channels);

        var output = new byte[outputSize];
        Convert(source, sourceFormat, output, destinationFormat, channels);
        return output;
    }

    /// <summary>
    /// Конвертирует аудио данные из одного формата в другой в предоставленный буфер.
    /// </summary>
    /// <param name="source">Исходные аудио данные.</param>
    /// <param name="sourceFormat">Формат исходных данных.</param>
    /// <param name="destination">Буфер назначения.</param>
    /// <param name="destinationFormat">Целевой формат.</param>
    /// <param name="channels">Количество аудиоканалов.</param>
    /// <returns>Количество записанных байт.</returns>
    public static int Convert(
        ReadOnlySpan<byte> source,
        AudioSampleFormat sourceFormat,
        Span<byte> destination,
        AudioSampleFormat destinationFormat,
        int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);

        if (sourceFormat == destinationFormat)
        {
            source.CopyTo(destination);
            return source.Length;
        }

        var sampleCount = CountSamples(source, sourceFormat);
        var needsLayoutConversion = sourceFormat.IsPlanar() != destinationFormat.IsPlanar();

        if (needsLayoutConversion)
        {
            return ConvertWithLayoutChange(
                source, sourceFormat, destination, destinationFormat, channels, sampleCount);
        }

        return ConvertSampleType(source, sourceFormat, destination, destinationFormat, sampleCount);
    }

    /// <summary>
    /// Деинтерливит аудио данные: из чередующихся каналов (L R L R)
    /// в раздельные плоскости (LLLL RRRR).
    /// </summary>
    /// <param name="interleaved">Interleaved аудио данные.</param>
    /// <param name="planar">Буфер для planar данных.</param>
    /// <param name="format">Формат семпла (должен быть interleaved).</param>
    /// <param name="channels">Количество каналов.</param>
    /// <returns>Количество записанных байт.</returns>
    public static int Deinterleave(
        ReadOnlySpan<byte> interleaved,
        Span<byte> planar,
        AudioSampleFormat format,
        int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);

        var bytesPerSample = format.GetBytesPerSample();
        var totalSamples = interleaved.Length / bytesPerSample;
        var samplesPerChannel = totalSamples / channels;

        for (var ch = 0; ch < channels; ch++)
        {
            for (var s = 0; s < samplesPerChannel; s++)
            {
                var srcOffset = ((s * channels) + ch) * bytesPerSample;
                var dstOffset = ((ch * samplesPerChannel) + s) * bytesPerSample;
                interleaved.Slice(srcOffset, bytesPerSample)
                    .CopyTo(planar[dstOffset..]);
            }
        }

        return totalSamples * bytesPerSample;
    }

    /// <summary>
    /// Интерливит аудио данные: из раздельных плоскостей (LLLL RRRR)
    /// в чередующиеся каналы (L R L R).
    /// </summary>
    /// <param name="planar">Planar аудио данные.</param>
    /// <param name="interleaved">Буфер для interleaved данных.</param>
    /// <param name="format">Формат семпла (должен быть planar).</param>
    /// <param name="channels">Количество каналов.</param>
    /// <returns>Количество записанных байт.</returns>
    public static int Interleave(
        ReadOnlySpan<byte> planar,
        Span<byte> interleaved,
        AudioSampleFormat format,
        int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);

        var bytesPerSample = format.GetBytesPerSample();
        var totalSamples = planar.Length / bytesPerSample;
        var samplesPerChannel = totalSamples / channels;

        for (var ch = 0; ch < channels; ch++)
        {
            for (var s = 0; s < samplesPerChannel; s++)
            {
                var srcOffset = ((ch * samplesPerChannel) + s) * bytesPerSample;
                var dstOffset = ((s * channels) + ch) * bytesPerSample;
                planar.Slice(srcOffset, bytesPerSample)
                    .CopyTo(interleaved[dstOffset..]);
            }
        }

        return totalSamples * bytesPerSample;
    }

    /// <summary>
    /// Микширует аудиоканалы: изменяет количество каналов.
    /// Поддерживает mono↔stereo, stereo→5.1, 5.1→stereo (downmix), и произвольные комбинации.
    /// Работает с interleaved форматами.
    /// </summary>
    /// <param name="source">Исходные аудио данные (interleaved).</param>
    /// <param name="format">Формат семплов.</param>
    /// <param name="sourceChannels">Количество каналов в источнике.</param>
    /// <param name="targetChannels">Целевое количество каналов.</param>
    /// <returns>Аудио данные с новым количеством каналов.</returns>
    public static byte[] MixChannels(
        ReadOnlySpan<byte> source,
        AudioSampleFormat format,
        int sourceChannels,
        int targetChannels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceChannels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetChannels, 1);

        if (sourceChannels == targetChannels)
        {
            return source.ToArray();
        }

        var bps = format.GetBytesPerSample();
        var sourceFrameSize = bps * sourceChannels;
        var frameCount = source.Length / sourceFrameSize;
        var targetFrameSize = bps * targetChannels;

        var output = new byte[frameCount * targetFrameSize];
        MixChannelsCore(source, output, format, sourceChannels, targetChannels, frameCount);
        return output;
    }

    /// <summary>
    /// Микширует аудиоканалы в предоставленный буфер.
    /// </summary>
    /// <param name="source">Исходные аудио данные (interleaved).</param>
    /// <param name="destination">Буфер назначения.</param>
    /// <param name="format">Формат семплов.</param>
    /// <param name="sourceChannels">Количество каналов в источнике.</param>
    /// <param name="targetChannels">Целевое количество каналов.</param>
    /// <returns>Количество записанных байт.</returns>
    public static int MixChannels(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        AudioSampleFormat format,
        int sourceChannels,
        int targetChannels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceChannels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetChannels, 1);

        if (sourceChannels == targetChannels)
        {
            source.CopyTo(destination);
            return source.Length;
        }

        var bps = format.GetBytesPerSample();
        var sourceFrameSize = bps * sourceChannels;
        var frameCount = source.Length / sourceFrameSize;

        return MixChannelsCore(source, destination, format, sourceChannels, targetChannels, frameCount);
    }

    private static int MixChannelsCore(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        AudioSampleFormat format,
        int sourceChannels,
        int targetChannels,
        int frameCount)
    {
        var bps = format.GetBytesPerSample();
        var targetFrameSize = bps * targetChannels;

        if (sourceChannels == 1 && targetChannels == 2)
            return MixMonoToStereo(source, destination, format, frameCount, targetFrameSize);

        if (sourceChannels == 2 && targetChannels == 1)
            return MixStereoToMono(source, destination, format, frameCount, targetFrameSize);

        if (sourceChannels == 2 && targetChannels == 6)
            return MixStereoTo51(source, destination, format, frameCount, targetFrameSize);

        if (sourceChannels == 6 && targetChannels == 2)
            return Mix51ToStereo(source, destination, format, frameCount, targetFrameSize);

        return targetChannels < sourceChannels
            ? DownmixGeneric(source, destination, format, sourceChannels, targetChannels, frameCount)
            : UpmixGeneric(source, destination, format, sourceChannels, targetChannels, frameCount);
    }

    private static int MixMonoToStereo(
        ReadOnlySpan<byte> source, Span<byte> destination,
        AudioSampleFormat format, int frameCount, int targetFrameSize)
    {
        for (var f = 0; f < frameCount; f++)
        {
            var val = ReadNormalized(source, format, f);
            WriteNormalized(destination, format, f * 2, val);
            WriteNormalized(destination, format, (f * 2) + 1, val);
        }

        return frameCount * targetFrameSize;
    }

    private static int MixStereoToMono(
        ReadOnlySpan<byte> source, Span<byte> destination,
        AudioSampleFormat format, int frameCount, int targetFrameSize)
    {
        for (var f = 0; f < frameCount; f++)
        {
            var left = ReadNormalized(source, format, f * 2);
            var right = ReadNormalized(source, format, (f * 2) + 1);
            WriteNormalized(destination, format, f, (left + right) * 0.5);
        }

        return frameCount * targetFrameSize;
    }

    private static int MixStereoTo51(
        ReadOnlySpan<byte> source, Span<byte> destination,
        AudioSampleFormat format, int frameCount, int targetFrameSize)
    {
        for (var f = 0; f < frameCount; f++)
        {
            var left = ReadNormalized(source, format, f * 2);
            var right = ReadNormalized(source, format, (f * 2) + 1);
            var center = (left + right) * 0.5;

            var dstBase = f * 6;
            WriteNormalized(destination, format, dstBase, left);          // FL
            WriteNormalized(destination, format, dstBase + 1, right);     // FR
            WriteNormalized(destination, format, dstBase + 2, center);    // FC
            WriteNormalized(destination, format, dstBase + 3, center * 0.5); // LFE
            WriteNormalized(destination, format, dstBase + 4, left * 0.7);  // RL
            WriteNormalized(destination, format, dstBase + 5, right * 0.7); // RR
        }

        return frameCount * targetFrameSize;
    }

    // ITU-R BS.775: L = FL + 0.707*FC + 0.707*RL, R = FR + 0.707*FC + 0.707*RR
    private static int Mix51ToStereo(
        ReadOnlySpan<byte> source, Span<byte> destination,
        AudioSampleFormat format, int frameCount, int targetFrameSize)
    {
        const double centerMix = 0.707;
        const double rearMix = 0.707;

        for (var f = 0; f < frameCount; f++)
        {
            var srcBase = f * 6;
            var fl = ReadNormalized(source, format, srcBase);
            var fr = ReadNormalized(source, format, srcBase + 1);
            var fc = ReadNormalized(source, format, srcBase + 2);
            // srcBase + 3 = LFE (не включается в стерео downmix)
            var rl = ReadNormalized(source, format, srcBase + 4);
            var rr = ReadNormalized(source, format, srcBase + 5);

            var left = fl + (centerMix * fc) + (rearMix * rl);
            var right = fr + (centerMix * fc) + (rearMix * rr);

            var peak = Math.Max(Math.Abs(left), Math.Abs(right));
            if (peak > 1.0)
            {
                left /= peak;
                right /= peak;
            }

            var dstBase = f * 2;
            WriteNormalized(destination, format, dstBase, left);
            WriteNormalized(destination, format, dstBase + 1, right);
        }

        return frameCount * targetFrameSize;
    }

    private static int DownmixGeneric(
        ReadOnlySpan<byte> source, Span<byte> destination,
        AudioSampleFormat format, int sourceChannels, int targetChannels, int frameCount)
    {
        for (var f = 0; f < frameCount; f++)
        {
            for (var tc = 0; tc < targetChannels; tc++)
            {
                var sum = 0.0;
                var count = 0;

                for (var sc = tc; sc < sourceChannels; sc += targetChannels)
                {
                    sum += ReadNormalized(source, format, (f * sourceChannels) + sc);
                    count++;
                }

                WriteNormalized(destination, format, (f * targetChannels) + tc, sum / count);
            }
        }

        return frameCount * format.GetBytesPerSample() * targetChannels;
    }

    private static int UpmixGeneric(
        ReadOnlySpan<byte> source, Span<byte> destination,
        AudioSampleFormat format, int sourceChannels, int targetChannels, int frameCount)
    {
        for (var f = 0; f < frameCount; f++)
        {
            for (var tc = 0; tc < targetChannels; tc++)
            {
                var sc = tc % sourceChannels;
                var val = ReadNormalized(source, format, (f * sourceChannels) + sc);
                WriteNormalized(destination, format, (f * targetChannels) + tc, val);
            }
        }

        return frameCount * format.GetBytesPerSample() * targetChannels;
    }

    private static int CountSamples(ReadOnlySpan<byte> data, AudioSampleFormat format)
    {
        var bytesPerSample = format.GetBytesPerSample();
        if (bytesPerSample == 0)
        {
            throw new ArgumentException(
                "Невозможно определить количество семплов для формата " + format + ".",
                nameof(format));
        }

        return data.Length / bytesPerSample;
    }

    private static int ConvertWithLayoutChange(
        ReadOnlySpan<byte> source,
        AudioSampleFormat sourceFormat,
        Span<byte> destination,
        AudioSampleFormat destinationFormat,
        int channels,
        int sampleCount)
    {
        // Сначала конвертируем sample type в промежуточный буфер,
        // затем меняем layout (interleave/deinterleave).
        var intermediateFormat = sourceFormat.IsPlanar()
            ? ToInterleaved(destinationFormat)
            : destinationFormat;

        var intermediateBps = intermediateFormat.GetBytesPerSample();
        var intermediateSize = sampleCount * intermediateBps;
        var intermediate = ArrayPool<byte>.Shared.Rent(intermediateSize);

        try
        {
            var intermediateSpan = intermediate.AsSpan(0, intermediateSize);

            if (NeedsSampleConversion(sourceFormat, intermediateFormat))
            {
                ConvertSampleType(
                    source, sourceFormat, intermediateSpan,
                    intermediateFormat, sampleCount);
            }
            else
            {
                source[..intermediateSize].CopyTo(intermediateSpan);
            }

            if (destinationFormat.IsPlanar())
            {
                return Deinterleave(
                    intermediateSpan, destination, intermediateFormat, channels);
            }

            return Interleave(
                intermediateSpan, destination, intermediateFormat, channels);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(intermediate);
        }
    }

    private static bool NeedsSampleConversion(
        AudioSampleFormat a, AudioSampleFormat b)
    {
        return a.GetBytesPerSample() != b.GetBytesPerSample()
            || a.IsFloat() != b.IsFloat();
    }

    private static AudioSampleFormat ToInterleaved(AudioSampleFormat format) => format switch
    {
        AudioSampleFormat.U8Planar => AudioSampleFormat.U8,
        AudioSampleFormat.S16Planar => AudioSampleFormat.S16,
        AudioSampleFormat.S32Planar => AudioSampleFormat.S32,
        AudioSampleFormat.F32Planar => AudioSampleFormat.F32,
        AudioSampleFormat.F64Planar => AudioSampleFormat.F64,
        _ => format,
    };

    private static int ConvertSampleType(
        ReadOnlySpan<byte> source,
        AudioSampleFormat sourceFormat,
        Span<byte> destination,
        AudioSampleFormat destinationFormat,
        int sampleCount)
    {
        for (var i = 0; i < sampleCount; i++)
        {
            var normalized = ReadNormalized(source, sourceFormat, i);
            WriteNormalized(destination, destinationFormat, i, normalized);
        }

        return sampleCount * destinationFormat.GetBytesPerSample();
    }

    internal static double ReadNormalized(
        ReadOnlySpan<byte> data, AudioSampleFormat format, int index)
    {
        var bps = format.GetBytesPerSample();
        var offset = index * bps;
        var sample = data.Slice(offset, bps);

        return format switch
        {
            AudioSampleFormat.U8 or AudioSampleFormat.U8Planar
                => (sample[0] - 128.0) / 128.0,

            AudioSampleFormat.S16 or AudioSampleFormat.S16Planar
                => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768.0,

            AudioSampleFormat.S32 or AudioSampleFormat.S32Planar
                => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648.0,

            AudioSampleFormat.F32 or AudioSampleFormat.F32Planar
                => MemoryMarshal.Read<float>(sample),

            AudioSampleFormat.F64 or AudioSampleFormat.F64Planar
                => MemoryMarshal.Read<double>(sample),

            _ => throw new ArgumentOutOfRangeException(
                nameof(format), format, "Неподдерживаемый исходный формат."),
        };
    }

    internal static void WriteNormalized(
        Span<byte> data, AudioSampleFormat format, int index, double value)
    {
        var bps = format.GetBytesPerSample();
        var offset = index * bps;
        var target = data.Slice(offset, bps);

        switch (format)
        {
            case AudioSampleFormat.U8 or AudioSampleFormat.U8Planar:
                target[0] = (byte)Math.Clamp((value * 128.0) + 128.0, 0.0, 255.0);
                break;

            case AudioSampleFormat.S16 or AudioSampleFormat.S16Planar:
                BinaryPrimitives.WriteInt16LittleEndian(
                    target, (short)Math.Clamp(value * 32768.0, -32768.0, 32767.0));
                break;

            case AudioSampleFormat.S32 or AudioSampleFormat.S32Planar:
                BinaryPrimitives.WriteInt32LittleEndian(
                    target, (int)Math.Clamp(value * 2147483648.0, -2147483648.0, 2147483647.0));
                break;

            case AudioSampleFormat.F32 or AudioSampleFormat.F32Planar:
                MemoryMarshal.Write(target, (float)value);
                break;

            case AudioSampleFormat.F64 or AudioSampleFormat.F64Planar:
                MemoryMarshal.Write(target, value);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(format), format, "Неподдерживаемый целевой формат.");
        }
    }

    /// <summary>
    /// Изменяет частоту дискретизации аудио данных (линейная интерполяция).
    /// Работает с interleaved форматами. Для planar — предварительно interleave.
    /// </summary>
    /// <param name="source">Исходные аудио данные (interleaved).</param>
    /// <param name="format">Формат семплов.</param>
    /// <param name="channels">Количество каналов.</param>
    /// <param name="sourceSampleRate">Исходная частота дискретизации.</param>
    /// <param name="targetSampleRate">Целевая частота дискретизации.</param>
    /// <returns>Ресемплированные аудио данные.</returns>
    public static byte[] ResampleRate(
        ReadOnlySpan<byte> source,
        AudioSampleFormat format,
        int channels,
        int sourceSampleRate,
        int targetSampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceSampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetSampleRate, 1);

        if (sourceSampleRate == targetSampleRate)
        {
            return source.ToArray();
        }

        var bps = format.GetBytesPerSample();
        var frameSize = bps * channels;
        var sourceFrames = source.Length / frameSize;
        var targetFrames = (int)((long)sourceFrames * targetSampleRate / sourceSampleRate);

        if (targetFrames == 0)
        {
            return [];
        }

        var output = new byte[targetFrames * frameSize];
        ResampleRateCore(source, output, format, channels, sourceFrames, targetFrames);
        return output;
    }

    /// <summary>
    /// Изменяет частоту дискретизации аудио данных в предоставленный буфер.
    /// </summary>
    /// <param name="source">Исходные аудио данные (interleaved).</param>
    /// <param name="destination">Буфер назначения.</param>
    /// <param name="format">Формат семплов.</param>
    /// <param name="channels">Количество каналов.</param>
    /// <param name="sourceSampleRate">Исходная частота дискретизации.</param>
    /// <param name="targetSampleRate">Целевая частота дискретизации.</param>
    /// <returns>Количество записанных байт.</returns>
    public static int ResampleRate(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        AudioSampleFormat format,
        int channels,
        int sourceSampleRate,
        int targetSampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceSampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(targetSampleRate, 1);

        if (sourceSampleRate == targetSampleRate)
        {
            source.CopyTo(destination);
            return source.Length;
        }

        var bps = format.GetBytesPerSample();
        var frameSize = bps * channels;
        var sourceFrames = source.Length / frameSize;
        var targetFrames = (int)((long)sourceFrames * targetSampleRate / sourceSampleRate);

        return ResampleRateCore(source, destination, format, channels, sourceFrames, targetFrames);
    }

    private static int ResampleRateCore(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        AudioSampleFormat format,
        int channels,
        int sourceFrames,
        int targetFrames)
    {
        var bps = format.GetBytesPerSample();
        var frameSize = bps * channels;
        var ratio = (double)(sourceFrames - 1) / Math.Max(targetFrames - 1, 1);

        for (var t = 0; t < targetFrames; t++)
        {
            var srcPos = t * ratio;
            var srcIndex = (int)srcPos;
            var frac = srcPos - srcIndex;

            // Catmull-Rom cubic: 4 точки (p0, p1, p2, p3)
            var i0 = Math.Max(srcIndex - 1, 0);
            var i1 = srcIndex;
            var i2 = Math.Min(srcIndex + 1, sourceFrames - 1);
            var i3 = Math.Min(srcIndex + 2, sourceFrames - 1);

            for (var ch = 0; ch < channels; ch++)
            {
                var p0 = ReadNormalized(source, format, (i0 * channels) + ch);
                var p1 = ReadNormalized(source, format, (i1 * channels) + ch);
                var p2 = ReadNormalized(source, format, (i2 * channels) + ch);
                var p3 = ReadNormalized(source, format, (i3 * channels) + ch);

                var interpolated = CatmullRom(p0, p1, p2, p3, frac);

                var dstSampleIdx = (t * channels) + ch;
                WriteNormalized(destination, format, dstSampleIdx, interpolated);
            }
        }

        return targetFrames * frameSize;
    }

    /// <summary>
    /// Catmull-Rom сплайн: гладкая кубическая интерполяция по 4 точкам.
    /// </summary>
    private static double CatmullRom(double p0, double p1, double p2, double p3, double t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return 0.5 * (
            (2.0 * p1)
            + ((-p0 + p2) * t)
            + (((2.0 * p0) - (5.0 * p1) + (4.0 * p2) - p3) * t2)
            + ((-p0 + (3.0 * p1) - (3.0 * p2) + p3) * t3));
    }
}
