using System.Numerics;

namespace Atom.Media.Audio.Effects;

/// <summary>
/// Параметрический эквалайзер на основе каскадных биквадратных фильтров.
/// Каждая полоса реализована как peaking EQ (Audio EQ Cookbook).
/// Для многоканального аудио применяется SIMD-оптимизация (каналы обрабатываются параллельно).
/// </summary>
public sealed class Equalizer : IAudioEffect
{
    private readonly EqBand[] bands;
    private double[][]? z1State;
    private double[][]? z2State;
    private double[][]? coefficients;
    private int configuredSampleRate;
    private int configuredChannels;

    /// <summary>
    /// Определяет, включён ли эффект.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Полосы эквалайзера.
    /// </summary>
    public IReadOnlyList<EqBand> Bands => bands;

    /// <summary>
    /// Создаёт эквалайзер с указанными полосами.
    /// </summary>
    /// <param name="bands">Полосы эквалайзера.</param>
    public Equalizer(params EqBand[] bands)
    {
        ArgumentNullException.ThrowIfNull(bands);
        this.bands = [.. bands];
    }

    /// <inheritdoc/>
    public void Process(Span<float> samples, int channels, int sampleRate)
    {
        if (!IsEnabled || bands.Length == 0) return;

        EnsureInitialized(channels, sampleRate);

        if (Vector.IsHardwareAccelerated
            && channels >= 2
            && channels <= Vector<double>.Count)
        {
            ProcessFramesSimd(samples, channels);
        }
        else
        {
            ProcessFrames(samples, channels);
        }
    }

    /// <inheritdoc/>
    public void Reset()
    {
        z1State = null;
        z2State = null;
        coefficients = null;
        configuredSampleRate = 0;
        configuredChannels = 0;
    }

    private void EnsureInitialized(int channels, int sampleRate)
    {
        if (sampleRate == configuredSampleRate && channels == configuredChannels)
        {
            return;
        }

        configuredSampleRate = sampleRate;
        configuredChannels = channels;
        InitializeArrays(channels);
        ComputeCoefficients(sampleRate);
    }

    private void InitializeArrays(int channels)
    {
        var paddedChannels = Vector.IsHardwareAccelerated
            ? Math.Max(channels, Vector<double>.Count)
            : channels;

        z1State = new double[bands.Length][];
        z2State = new double[bands.Length][];
        coefficients = new double[bands.Length][];

        for (var b = 0; b < bands.Length; b++)
        {
            z1State[b] = new double[paddedChannels];
            z2State[b] = new double[paddedChannels];
            coefficients[b] = new double[5];
        }
    }

    private void ComputeCoefficients(int sampleRate)
    {
        for (var b = 0; b < bands.Length; b++)
        {
            ComputeBandCoefficients(b, bands[b], sampleRate);
        }
    }

    private void ComputeBandCoefficients(int bandIndex, EqBand band, int sampleRate)
    {
        var a = Math.Pow(10.0, band.GainDb / 40.0);
        var w0 = 2.0 * Math.PI * band.Frequency / sampleRate;
        var sinW0 = Math.Sin(w0);
        var cosW0 = Math.Cos(w0);
        var alpha = sinW0 / (2.0 * band.Q);

        var a0 = 1.0 + (alpha / a);
        var c = coefficients![bandIndex];
        c[0] = (1.0 + (alpha * a)) / a0;   // b0
        c[1] = -2.0 * cosW0 / a0;        // b1
        c[2] = (1.0 - (alpha * a)) / a0;   // b2
        c[3] = -2.0 * cosW0 / a0;        // a1
        c[4] = (1.0 - (alpha / a)) / a0;   // a2
    }

    private void ProcessFrames(Span<float> samples, int channels)
    {
        var frameCount = samples.Length / channels;

        for (var f = 0; f < frameCount; f++)
        {
            for (var ch = 0; ch < channels; ch++)
            {
                var idx = (f * channels) + ch;
                var sample = (double)samples[idx];

                for (var b = 0; b < bands.Length; b++)
                {
                    var c = coefficients![b];
                    var output = (c[0] * sample) + z1State![b][ch];
                    z1State[b][ch] = (c[1] * sample) - (c[3] * output) + z2State![b][ch];
                    z2State[b][ch] = (c[2] * sample) - (c[4] * output);
                    sample = output;
                }

                samples[idx] = (float)sample;
            }
        }
    }

    private void ProcessFramesSimd(Span<float> samples, int channels)
    {
        var frameCount = samples.Length / channels;
        var bandCount = bands.Length;

        var z1 = new Vector<double>[bandCount];
        var z2 = new Vector<double>[bandCount];
        var cb0 = new Vector<double>[bandCount];
        var cb1 = new Vector<double>[bandCount];
        var cb2 = new Vector<double>[bandCount];
        var ca1 = new Vector<double>[bandCount];
        var ca2 = new Vector<double>[bandCount];

        for (var b = 0; b < bandCount; b++)
        {
            z1[b] = new Vector<double>(z1State![b]);
            z2[b] = new Vector<double>(z2State![b]);

            var c = coefficients![b];
            cb0[b] = new Vector<double>(c[0]);
            cb1[b] = new Vector<double>(c[1]);
            cb2[b] = new Vector<double>(c[2]);
            ca1[b] = new Vector<double>(c[3]);
            ca2[b] = new Vector<double>(c[4]);
        }

        Span<double> buf = stackalloc double[Vector<double>.Count];

        for (var f = 0; f < frameCount; f++)
        {
            buf.Clear();
            var baseIdx = f * channels;

            for (var ch = 0; ch < channels; ch++)
            {
                buf[ch] = samples[baseIdx + ch];
            }

            var sample = new Vector<double>(buf);

            for (var b = 0; b < bandCount; b++)
            {
                var output = (cb0[b] * sample) + z1[b];
                z1[b] = (cb1[b] * sample) - (ca1[b] * output) + z2[b];
                z2[b] = (cb2[b] * sample) - (ca2[b] * output);
                sample = output;
            }

            sample.CopyTo(buf);

            for (var ch = 0; ch < channels; ch++)
            {
                samples[baseIdx + ch] = (float)buf[ch];
            }
        }

        for (var b = 0; b < bandCount; b++)
        {
            z1[b].CopyTo(z1State![b]);
            z2[b].CopyTo(z2State![b]);
        }
    }
}
