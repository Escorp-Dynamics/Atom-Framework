using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Atom.Media.Audio;

/// <summary>
/// Измеряет уровень аудиосигнала: пиковый (peak) и среднеквадратичный (RMS).
/// Поддерживает все <see cref="AudioSampleFormat"/> через нормализацию в [-1.0, 1.0].
/// Для F32 формата применяется SIMD-оптимизация.
/// </summary>
public static class AudioMeter
{
    /// <summary>
    /// Измеряет пиковый уровень (максимальную абсолютную амплитуду) по всем каналам.
    /// </summary>
    /// <param name="source">Аудио данные (interleaved).</param>
    /// <param name="format">Формат семплов.</param>
    /// <param name="channels">Количество каналов.</param>
    /// <returns>Пиковый уровень в диапазоне [0.0, 1.0+].</returns>
    public static float MeasurePeak(
        ReadOnlySpan<byte> source, AudioSampleFormat format, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);

        if (format is AudioSampleFormat.F32)
        {
            return MeasurePeakF32(MemoryMarshal.Cast<byte, float>(source));
        }

        var sampleCount = source.Length / format.GetBytesPerSample();
        var peak = 0.0;

        for (var i = 0; i < sampleCount; i++)
        {
            var abs = Math.Abs(AudioResampler.ReadNormalized(source, format, i));
            if (abs > peak) peak = abs;
        }

        return (float)peak;
    }

    /// <summary>
    /// Измеряет пиковый уровень для каждого канала отдельно.
    /// </summary>
    /// <param name="source">Аудио данные (interleaved).</param>
    /// <param name="format">Формат семплов.</param>
    /// <param name="channels">Количество каналов.</param>
    /// <param name="perChannel">Буфер для результатов (длина ≥ channels).</param>
    public static void MeasurePeak(
        ReadOnlySpan<byte> source, AudioSampleFormat format,
        int channels, Span<float> perChannel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);

        if (format is AudioSampleFormat.F32)
        {
            MeasurePeakPerChannelF32(
                MemoryMarshal.Cast<byte, float>(source), channels, perChannel);
            return;
        }

        perChannel[..channels].Clear();
        var sampleCount = source.Length / format.GetBytesPerSample();

        for (var i = 0; i < sampleCount; i++)
        {
            var ch = i % channels;
            var abs = (float)Math.Abs(AudioResampler.ReadNormalized(source, format, i));
            perChannel[ch] = Math.Max(perChannel[ch], abs);
        }
    }

    /// <summary>
    /// Измеряет среднеквадратичный (RMS) уровень по всем каналам.
    /// </summary>
    /// <param name="source">Аудио данные (interleaved).</param>
    /// <param name="format">Формат семплов.</param>
    /// <param name="channels">Количество каналов.</param>
    /// <returns>RMS уровень в диапазоне [0.0, 1.0+].</returns>
    public static float MeasureRms(
        ReadOnlySpan<byte> source, AudioSampleFormat format, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);

        if (format is AudioSampleFormat.F32)
        {
            return MeasureRmsF32(MemoryMarshal.Cast<byte, float>(source));
        }

        var sampleCount = source.Length / format.GetBytesPerSample();
        if (sampleCount == 0) return 0.0f;

        var sumSquares = 0.0;

        for (var i = 0; i < sampleCount; i++)
        {
            var val = AudioResampler.ReadNormalized(source, format, i);
            sumSquares += val * val;
        }

        return (float)Math.Sqrt(sumSquares / sampleCount);
    }

    /// <summary>
    /// Измеряет среднеквадратичный (RMS) уровень для каждого канала отдельно.
    /// </summary>
    /// <param name="source">Аудио данные (interleaved).</param>
    /// <param name="format">Формат семплов.</param>
    /// <param name="channels">Количество каналов.</param>
    /// <param name="perChannel">Буфер для результатов (длина ≥ channels).</param>
    public static void MeasureRms(
        ReadOnlySpan<byte> source, AudioSampleFormat format,
        int channels, Span<float> perChannel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);

        if (format is AudioSampleFormat.F32)
        {
            MeasureRmsPerChannelF32(
                MemoryMarshal.Cast<byte, float>(source), channels, perChannel);
            return;
        }

        perChannel[..channels].Clear();
        var sampleCount = source.Length / format.GetBytesPerSample();
        if (sampleCount == 0) return;

        Span<int> counts = stackalloc int[channels];
        Span<double> sums = stackalloc double[channels];

        for (var i = 0; i < sampleCount; i++)
        {
            var ch = i % channels;
            var val = AudioResampler.ReadNormalized(source, format, i);
            sums[ch] += val * val;
            counts[ch]++;
        }

        for (var ch = 0; ch < channels; ch++)
        {
            perChannel[ch] = counts[ch] > 0
                ? (float)Math.Sqrt(sums[ch] / counts[ch])
                : 0.0f;
        }
    }

    /// <summary>
    /// Конвертирует линейный уровень в децибелы (dBFS).
    /// </summary>
    /// <param name="level">Линейный уровень (0.0 = тишина, 1.0 = полная шкала).</param>
    /// <returns>Уровень в dBFS. Для нулевого уровня возвращает <see cref="float.NegativeInfinity"/>.</returns>
    public static float ToDecibels(float level) =>
        level <= 0.0f ? float.NegativeInfinity : 20.0f * MathF.Log10(level);

    private static float MeasurePeakF32(ReadOnlySpan<float> samples)
    {
        if (Vector.IsHardwareAccelerated && samples.Length >= Vector<float>.Count)
        {
            var vectorSize = Vector<float>.Count;
            var absMax = Vector<float>.Zero;
            var i = 0;

            for (; i + vectorSize <= samples.Length; i += vectorSize)
            {
                absMax = Vector.Max(absMax, Vector.Abs(
                    new Vector<float>(samples.Slice(i, vectorSize))));
            }

            var max = 0.0f;
            for (var j = 0; j < vectorSize; j++)
            {
                max = Math.Max(max, absMax[j]);
            }

            for (; i < samples.Length; i++)
            {
                max = Math.Max(max, Math.Abs(samples[i]));
            }

            return max;
        }

        var peak = 0.0f;
        foreach (var s in samples)
        {
            peak = Math.Max(peak, Math.Abs(s));
        }

        return peak;
    }

    private static float MeasureRmsF32(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0) return 0.0f;

        if (Vector.IsHardwareAccelerated && samples.Length >= Vector<float>.Count)
        {
            var vectorSize = Vector<float>.Count;
            var sumSq = Vector<float>.Zero;
            var i = 0;

            for (; i + vectorSize <= samples.Length; i += vectorSize)
            {
                var v = new Vector<float>(samples.Slice(i, vectorSize));
                sumSq += v * v;
            }

            var sum = 0.0;
            for (var j = 0; j < vectorSize; j++)
            {
                sum += sumSq[j];
            }

            for (; i < samples.Length; i++)
            {
                sum += (double)samples[i] * samples[i];
            }

            return (float)Math.Sqrt(sum / samples.Length);
        }

        var total = 0.0;
        foreach (var s in samples)
        {
            total += (double)s * s;
        }

        return (float)Math.Sqrt(total / samples.Length);
    }

    private static void MeasurePeakPerChannelF32(
        ReadOnlySpan<float> samples, int channels, Span<float> perChannel)
    {
        perChannel[..channels].Clear();
        var frameCount = samples.Length / channels;

        if (channels == 1)
        {
            perChannel[0] = MeasurePeakF32(samples);
            return;
        }

        float[]? rented = null;
        var buf = frameCount <= 1024
            ? stackalloc float[frameCount]
            : (rented = ArrayPool<float>.Shared.Rent(frameCount)).AsSpan(0, frameCount);

        try
        {
            for (var ch = 0; ch < channels; ch++)
            {
                GatherChannel(samples, buf, ch, channels, frameCount);
                perChannel[ch] = MeasurePeakF32(buf);
            }
        }
        finally
        {
            if (rented is not null)
                ArrayPool<float>.Shared.Return(rented);
        }
    }

    private static void MeasureRmsPerChannelF32(
        ReadOnlySpan<float> samples, int channels, Span<float> perChannel)
    {
        perChannel[..channels].Clear();
        var frameCount = samples.Length / channels;
        if (frameCount == 0) return;

        if (channels == 1)
        {
            perChannel[0] = MeasureRmsF32(samples);
            return;
        }

        float[]? rented = null;
        var buf = frameCount <= 1024
            ? stackalloc float[frameCount]
            : (rented = ArrayPool<float>.Shared.Rent(frameCount)).AsSpan(0, frameCount);

        try
        {
            for (var ch = 0; ch < channels; ch++)
            {
                GatherChannel(samples, buf, ch, channels, frameCount);
                perChannel[ch] = MeasureRmsF32(buf);
            }
        }
        finally
        {
            if (rented is not null)
                ArrayPool<float>.Shared.Return(rented);
        }
    }

    private static void GatherChannel(
        ReadOnlySpan<float> interleaved, Span<float> output,
        int channel, int channels, int frameCount)
    {
        for (var f = 0; f < frameCount; f++)
        {
            output[f] = interleaved[(f * channels) + channel];
        }
    }
}
