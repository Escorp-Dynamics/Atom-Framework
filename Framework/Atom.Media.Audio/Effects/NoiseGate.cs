using System.Buffers;
using System.Numerics;

namespace Atom.Media.Audio.Effects;

/// <summary>
/// Noise gate — подавляет сигнал ниже порога.
/// Используется для устранения фонового шума между фразами.
/// </summary>
public sealed class NoiseGate : IAudioEffect
{
    private double gateGain;
    private int holdCounter;

    /// <summary>
    /// Определяет, включён ли эффект.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Порог срабатывания в dB (сигнал ниже порога подавляется).
    /// </summary>
    public float ThresholdDb { get; set; } = -40.0f;

    /// <summary>
    /// Время атаки (открытия гейта) в миллисекундах.
    /// </summary>
    public float AttackMs { get; set; } = 1.0f;

    /// <summary>
    /// Время восстановления (закрытия гейта) в миллисекундах.
    /// </summary>
    public float ReleaseMs { get; set; } = 50.0f;

    /// <summary>
    /// Время удержания — гейт остаётся открытым после падения ниже порога.
    /// </summary>
    public float HoldMs { get; set; } = 10.0f;

    /// <inheritdoc/>
    public void Process(Span<float> samples, int channels, int sampleRate)
    {
        if (!IsEnabled) return;

        var frameCount = samples.Length / channels;
        var thresholdLinear = MathF.Pow(10.0f, ThresholdDb / 20.0f);
        var attackCoeff = ComputeCoeff(AttackMs, sampleRate);
        var releaseCoeff = ComputeCoeff(ReleaseMs, sampleRate);
        var holdSamples = (int)(HoldMs * 0.001 * sampleRate);

        float[]? rented = null;
        var gains = frameCount <= 1024
            ? stackalloc float[frameCount]
            : (rented = ArrayPool<float>.Shared.Rent(frameCount)).AsSpan(0, frameCount);

        try
        {
            for (var f = 0; f < frameCount; f++)
            {
                var peak = FindFramePeak(samples, f, channels);
                UpdateGate(peak, thresholdLinear, attackCoeff, releaseCoeff, holdSamples);
                gains[f] = (float)gateGain;
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
    public void Reset()
    {
        gateGain = 0.0;
        holdCounter = 0;
    }

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

    private void UpdateGate(
        float peak, float threshold,
        double attackCoeff, double releaseCoeff, int holdSamples)
    {
        double target;

        if (peak >= threshold)
        {
            holdCounter = holdSamples;
            target = 1.0;
        }
        else if (holdCounter > 0)
        {
            holdCounter--;
            target = 1.0;
        }
        else
        {
            target = 0.0;
        }

        var coeff = target > gateGain ? attackCoeff : releaseCoeff;
        gateGain += (target - gateGain) * coeff;
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
