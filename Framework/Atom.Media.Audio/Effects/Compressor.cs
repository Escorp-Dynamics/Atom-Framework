using System.Buffers;
using System.Numerics;

namespace Atom.Media.Audio.Effects;

/// <summary>
/// Динамический компрессор — уменьшает динамический диапазон сигнала.
/// Сигнал выше порога сжимается с заданным ratio.
/// </summary>
public sealed class Compressor : IAudioEffect
{
    private double envelope = 1.0;

    /// <summary>
    /// Определяет, включён ли эффект.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Порог компрессии в dB. Сигнал выше порога сжимается.
    /// </summary>
    public float ThresholdDb { get; set; } = -20.0f;

    /// <summary>
    /// Степень сжатия. Например, 4.0 означает 4:1.
    /// </summary>
    public float Ratio { get; set; } = 4.0f;

    /// <summary>
    /// Время атаки (включения компрессии) в миллисекундах.
    /// </summary>
    public float AttackMs { get; set; } = 5.0f;

    /// <summary>
    /// Время восстановления (отпускания компрессии) в миллисекундах.
    /// </summary>
    public float ReleaseMs { get; set; } = 50.0f;

    /// <summary>
    /// Компенсация громкости после компрессии в dB.
    /// </summary>
    public float MakeupGainDb { get; set; }

    /// <inheritdoc/>
    public void Process(Span<float> samples, int channels, int sampleRate)
    {
        if (!IsEnabled) return;

        var frameCount = samples.Length / channels;
        var attackCoeff = ComputeCoeff(AttackMs, sampleRate);
        var releaseCoeff = ComputeCoeff(ReleaseMs, sampleRate);
        var thresholdLinear = Math.Pow(10.0, ThresholdDb / 20.0);
        var makeupLinear = Math.Pow(10.0, MakeupGainDb / 20.0);

        float[]? rented = null;
        var gains = frameCount <= 1024
            ? stackalloc float[frameCount]
            : (rented = ArrayPool<float>.Shared.Rent(frameCount)).AsSpan(0, frameCount);

        try
        {
            for (var f = 0; f < frameCount; f++)
            {
                var peak = FindFramePeak(samples, f, channels);
                var targetGain = ComputeTargetGain(peak, thresholdLinear);
                SmoothEnvelope(targetGain, attackCoeff, releaseCoeff);
                gains[f] = (float)(envelope * makeupLinear);
            }

            ApplyGainsBatch(samples, gains, channels);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<float>.Shared.Return(rented);
        }
    }

    /// <inheritdoc/>
    public void Reset() =>
        envelope = 1.0;

    private static double ComputeCoeff(float timeMs, int sampleRate)
    {
        var timeSec = timeMs * 0.001;
        return timeSec > 0.0
            ? 1.0 - Math.Exp(-1.0 / (timeSec * sampleRate))
            : 1.0;
    }

    private static float FindFramePeak(Span<float> samples, int frame, int channels)
    {
        var peak = 0.0f;
        var baseIdx = frame * channels;

        for (var ch = 0; ch < channels; ch++)
        {
            peak = Math.Max(peak, MathF.Abs(samples[baseIdx + ch]));
        }

        return peak;
    }

    private double ComputeTargetGain(float peak, double thresholdLinear)
    {
        if (peak <= thresholdLinear || peak <= 0.0f)
        {
            return 1.0;
        }

        var overDb = 20.0 * Math.Log10(peak / thresholdLinear);
        var reductionDb = overDb * (1.0 - (1.0 / Ratio));
        return Math.Pow(10.0, -reductionDb / 20.0);
    }

    private void SmoothEnvelope(double targetGain, double attackCoeff, double releaseCoeff)
    {
        var coeff = targetGain < envelope ? attackCoeff : releaseCoeff;
        envelope += (targetGain - envelope) * coeff;
    }

    private static void ApplyGainsBatch(
        Span<float> samples, ReadOnlySpan<float> gains, int channels)
    {
        if (channels == 1)
        {
            ApplyGainsMono(samples, gains);
            return;
        }

        for (var f = 0; f < gains.Length; f++)
        {
            var gain = gains[f];
            var baseIdx = f * channels;

            for (var ch = 0; ch < channels; ch++)
            {
                samples[baseIdx + ch] *= gain;
            }
        }
    }

    private static void ApplyGainsMono(Span<float> samples, ReadOnlySpan<float> gains)
    {
        if (Vector.IsHardwareAccelerated && samples.Length >= Vector<float>.Count)
        {
            var vectorSize = Vector<float>.Count;
            var i = 0;

            for (; i + vectorSize <= samples.Length; i += vectorSize)
            {
                var s = new Vector<float>(samples.Slice(i, vectorSize));
                var g = new Vector<float>(gains.Slice(i, vectorSize));
                (s * g).CopyTo(samples.Slice(i, vectorSize));
            }

            for (; i < samples.Length; i++)
            {
                samples[i] *= gains[i];
            }

            return;
        }

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] *= gains[i];
        }
    }
}
