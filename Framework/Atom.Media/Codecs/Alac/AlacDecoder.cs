#pragma warning disable CA1822, MA0042, IDE0032

using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Atom.IO;
using Microsoft.Extensions.Logging;

namespace Atom.Media;

/// <summary>
/// Декодер ALAC (Apple Lossless Audio Codec).
/// Поддерживает сжатые и несжатые фреймы, адаптивное Rice-кодирование,
/// LPC предсказание, стерео декорреляцию.
/// </summary>
public sealed class AlacDecoder : IAudioCodec
{
    private const int RiceLimit = 9;

    private AudioCodecParameters parameters;
    private int bitDepth;
    private int defaultFrameLength;
    private int riceHistoryMult;
    private int riceInitialHistory;
    private int riceKModifier;
    private bool isInitialized;
    private bool isDisposed;

    /// <inheritdoc/>
    public MediaCodecId CodecId => MediaCodecId.Alac;

    /// <inheritdoc/>
    public string Name => "ALAC Audio";

    /// <inheritdoc/>
    public CodecCapabilities Capabilities => CodecCapabilities.Decode | CodecCapabilities.Lossless;

    /// <inheritdoc/>
    public ILogger? Logger { get; set; }

    /// <inheritdoc/>
    public IMeterFactory? MeterFactory { get; set; }

    /// <inheritdoc/>
    public HardwareAcceleration Acceleration { get; init; }

    /// <inheritdoc/>
    public AudioCodecParameters Parameters => parameters;

    #region Initialization

    /// <inheritdoc/>
    public CodecResult InitializeDecoder(in AudioCodecParameters parameters)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (parameters.SampleRate <= 0 || parameters.ChannelCount <= 0)
            return CodecResult.InvalidData;

        this.parameters = parameters;

        if (parameters.ExtraData.Length >= 24)
        {
            ParseMagicCookie(parameters.ExtraData.Span);
        }
        else
        {
            bitDepth = parameters.SampleFormat switch
            {
                AudioSampleFormat.U8 => 8,
                AudioSampleFormat.S16 => 16,
                AudioSampleFormat.S32 => 32,
                _ => 16,
            };
            defaultFrameLength = 4096;
            riceHistoryMult = 40;
            riceInitialHistory = 10;
            riceKModifier = 14;
        }

        isInitialized = true;
        return CodecResult.Success;
    }

    /// <inheritdoc/>
    public CodecResult InitializeEncoder(in AudioCodecParameters parameters) =>
        CodecResult.UnsupportedFormat;

    private void ParseMagicCookie(ReadOnlySpan<byte> extra)
    {
        defaultFrameLength = BinaryPrimitives.ReadInt32BigEndian(extra);
        bitDepth = extra[5];
        riceHistoryMult = extra[6];
        riceInitialHistory = extra[7];
        riceKModifier = extra[8];
    }

    #endregion

    #region Decode

    /// <inheritdoc/>
    public CodecResult Decode(ReadOnlySpan<byte> packet, ref AudioFrame frame)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isInitialized)
            return CodecResult.NotInitialized;

        if (packet.Length < 4)
            return CodecResult.NeedMoreData;

        try
        {
            return DecodeFrame(packet, ref frame);
        }
        catch (InvalidOperationException)
        {
            return CodecResult.InvalidData;
        }
    }

    /// <inheritdoc/>
    public ValueTask<CodecResult> DecodeAsync(
        ReadOnlyMemory<byte> packet,
        [NotNull] AudioFrameBuffer buffer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isInitialized)
            return new ValueTask<CodecResult>(CodecResult.NotInitialized);

        cancellationToken.ThrowIfCancellationRequested();

        if (!buffer.IsAllocated)
            buffer.Allocate(defaultFrameLength, parameters.ChannelCount, parameters.SampleRate, parameters.SampleFormat);

        var frame = buffer.AsFrame();
        var result = Decode(packet.Span, ref frame);
        return new ValueTask<CodecResult>(result);
    }

    #endregion

    #region Encode (unsupported)

    /// <inheritdoc/>
    public CodecResult Encode(in ReadOnlyAudioFrame frame, Span<byte> output, out int bytesWritten)
    {
        bytesWritten = 0;
        return CodecResult.UnsupportedFormat;
    }

    /// <inheritdoc/>
    public ValueTask<(CodecResult Result, int BytesWritten)> EncodeAsync(
        [NotNull] AudioFrameBuffer frame,
        Memory<byte> output,
        CancellationToken cancellationToken = default) =>
        new((CodecResult.UnsupportedFormat, 0));

    #endregion

    #region Flush, Reset, Dispose

    /// <inheritdoc/>
    public CodecResult Flush(ref AudioFrame frame) =>
        CodecResult.EndOfStream;

    /// <inheritdoc/>
    public void Reset()
    {
    }

    /// <inheritdoc/>
    public void Dispose() => isDisposed = true;

    #endregion

    #region Frame Decoding

    private CodecResult DecodeFrame(ReadOnlySpan<byte> packet, ref AudioFrame frame)
    {
        var reader = new BitReader(packet);

        // Element tag
        var tag = (int)reader.ReadBits(3);
        reader.ReadBits(4); // element instance tag
        reader.ReadBits(12); // unused

        var channelCount = tag switch
        {
            0 => 1, // SCE (Single Channel Element)
            1 => 2, // CPE (Channel Pair Element)
            _ => throw new InvalidOperationException("Unsupported ALAC element tag"),
        };

        // Header byte
        reader.ReadBits(1); // unused
        var hasSize = reader.ReadBit();
        var isUncompressed = reader.ReadBit();
        reader.ReadBits(1); // unused

        var outputSamples = hasSize ? (int)reader.ReadBits(32) : defaultFrameLength;

        if (isUncompressed)
            return DecodeUncompressed(ref reader, ref frame, outputSamples, channelCount);

        return DecodeCompressed(ref reader, ref frame, outputSamples, channelCount);
    }

    private CodecResult DecodeUncompressed(
        ref BitReader reader, ref AudioFrame frame, int sampleCount, int channelCount)
    {
        reader.ReadBits(16); // shift bits (unused for uncompressed)

        var output = frame.InterleavedData;
        var bytesPerSample = GetBytesPerSample();
        var offset = 0;

        for (var i = 0; i < sampleCount; i++)
        {
            for (var ch = 0; ch < channelCount; ch++)
            {
                var sample = ReadSigned(ref reader, bitDepth);
                WriteSample(output, ref offset, sample, bytesPerSample);
            }
        }

        return CodecResult.Success;
    }

    private CodecResult DecodeCompressed(
        ref BitReader reader, ref AudioFrame frame, int sampleCount, int channelCount)
    {
        var mixBits = 0;
        var mixRes = 0;

        if (channelCount == 2)
        {
            mixBits = (int)reader.ReadBits(8);
            mixRes = (int)reader.ReadBits(8);
        }

        var channelParams = ReadChannelParams(ref reader, channelCount);
        var shiftBits = (int)reader.ReadBits(16);

        // Decode Rice residuals
        var totalSamples = sampleCount * channelCount;
        var samples = ArrayPool<int>.Shared.Rent(totalSamples);
        try
        {
            for (var ch = 0; ch < channelCount; ch++)
            {
                var chSamples = samples.AsSpan(ch * sampleCount, sampleCount);
                DecodeRiceResiduals(ref reader, chSamples, sampleCount);
            }

            // Extra shift bits
            if (shiftBits > 0)
                ReadExtraShiftBits(ref reader, samples, sampleCount, channelCount, shiftBits);

            // LPC prediction per channel
            for (var ch = 0; ch < channelCount; ch++)
            {
                var p = channelParams[ch];
                var chSamples = samples.AsSpan(ch * sampleCount, sampleCount);

                if (p.NumCoeffs > 0)
                    ApplyLpcPrediction(chSamples, p.Coefficients, p.NumCoeffs, p.DenShift, sampleCount);
            }

            // Stereo unmixing
            if (channelCount == 2 && mixRes != 0)
                UnmixStereo(samples.AsSpan(0, sampleCount), samples.AsSpan(sampleCount, sampleCount), mixBits, mixRes, sampleCount);

            // Write to output
            WriteSamplesToFrame(ref frame, samples, sampleCount, channelCount);
            return CodecResult.Success;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(samples);
        }
    }

    #endregion

    #region Rice Decoding

    private void DecodeRiceResiduals(ref BitReader reader, Span<int> output, int sampleCount)
    {
        var history = riceInitialHistory;
        var signModifier = 0;
        var i = 0;

        while (i < sampleCount)
        {
            var k = ComputeRiceK(history);
            var value = ReadRiceValue(ref reader, k, bitDepth) + signModifier;
            signModifier = 0;

            output[i] = ZigzagDecode(value);
            i++;

            history = UpdateHistory(history, value);

            if (history < 128 && i < sampleCount)
                i = HandleZeroRun(ref reader, output, i, sampleCount, ref history, ref signModifier);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ComputeRiceK(int history)
    {
        var log = 31 - BitOperations.LeadingZeroCount((uint)((history >> 9) + 3));
        return Math.Min(log, riceKModifier);
    }

    private static int ReadRiceValue(ref BitReader reader, int k, int bitDepth)
    {
        var extra = 0;

        if (k > 0)
            extra = (int)reader.ReadBits(k);

        // Read unary (count zeros until 1)
        var unary = 0;

        while (!reader.ReadBit())
            unary++;

        if (unary >= RiceLimit)
            return (int)reader.ReadBits(bitDepth);

        return extra + (unary << k);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ZigzagDecode(int value) =>
        (value & 1) != 0 ? -((value >> 1) + 1) : value >> 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int UpdateHistory(int history, int value) =>
        value > 0xFFFF
            ? 0xFFFF
            : history + ((value * riceHistoryMult) - ((history * riceHistoryMult) >> 9));

    private static int HandleZeroRun(
        ref BitReader reader, Span<int> output, int i, int sampleCount,
        ref int history, ref int signModifier)
    {
        var kz = Math.Min(7, BitOperations.LeadingZeroCount((uint)history) - 24);

        if (kz < 0)
            kz = 0;

        var run = ReadRiceValue(ref reader, kz, 16);
        var zerosToFill = Math.Min(run, sampleCount - i);

        output.Slice(i, zerosToFill).Clear();
        i += zerosToFill;

        if (run < 0xFFFF)
            signModifier = 1;

        history = 0;
        return i;
    }

    #endregion

    #region LPC Prediction

    private static void ApplyLpcPrediction(
        Span<int> samples, int[] coefficients, int numCoeffs, int denShift, int sampleCount)
    {
        if (denShift == 0 || numCoeffs == 0 || sampleCount <= numCoeffs)
            return;

        for (var i = numCoeffs; i < sampleCount; i++)
        {
            long prediction = 0;
            var @base = samples[i - numCoeffs - 1];

            for (var j = 0; j < numCoeffs; j++)
                prediction += (long)coefficients[j] * (samples[i - j - 1] - @base);

            samples[i] += (int)(prediction >> denShift) + @base;

            // Coefficient adaptation
            AdaptCoefficients(samples, coefficients, numCoeffs, i);
        }
    }

    private static void AdaptCoefficients(Span<int> samples, int[] coefficients, int numCoeffs, int i)
    {
        var error = samples[i] - samples[i - 1];

        if (error == 0)
            return;

        var sign = error > 0 ? 1 : -1;

        for (var j = numCoeffs - 1; j >= 0; j--)
        {
            var diff = samples[i - j - 1] - samples[i - numCoeffs - 1];

            if (diff > 0)
                coefficients[j] += sign;
            else if (diff < 0)
                coefficients[j] -= sign;

            if (error == 0)
                break;

            error -= sign * (diff >> numCoeffs);
        }
    }

    #endregion

    #region Stereo Unmixing

    private static void UnmixStereo(
        Span<int> left, Span<int> right, int mixBits, int mixRes, int sampleCount)
    {
        for (var i = 0; i < sampleCount; i++)
        {
            var l = left[i];
            var r = right[i];
            right[i] = l - ((r * mixRes) >> mixBits);
            left[i] = right[i] + r;
        }
    }

    #endregion

    #region Utilities

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ChannelPredictionParams(
        int Mode,
        int DenShift,
        int NumCoeffs,
        int[] Coefficients);

    private static ChannelPredictionParams[] ReadChannelParams(ref BitReader reader, int channelCount)
    {
        var result = new ChannelPredictionParams[channelCount];

        for (var ch = 0; ch < channelCount; ch++)
        {
            var mode = (int)reader.ReadBits(4);
            var denShift = (int)reader.ReadBits(5);
            reader.ReadBits(3); // pbFactor
            var numCoeffs = (int)reader.ReadBits(5);
            var coefs = new int[numCoeffs];

            for (var j = 0; j < numCoeffs; j++)
                coefs[j] = ReadSigned(ref reader, 16);

            result[ch] = new ChannelPredictionParams(mode, denShift, numCoeffs, coefs);
        }

        return result;
    }

    private static void ReadExtraShiftBits(
        ref BitReader reader, Span<int> samples, int sampleCount, int channelCount, int shiftBits)
    {
        for (var i = 0; i < sampleCount; i++)
        {
            for (var ch = 0; ch < channelCount; ch++)
            {
                var extra = (int)reader.ReadBits(shiftBits);
                samples[(ch * sampleCount) + i] = (samples[(ch * sampleCount) + i] << shiftBits) | extra;
            }
        }
    }

    private void WriteSamplesToFrame(ref AudioFrame frame, Span<int> samples, int sampleCount, int channelCount)
    {
        var output = frame.InterleavedData;
        var bytesPerSample = GetBytesPerSample();
        var offset = 0;

        for (var i = 0; i < sampleCount; i++)
        {
            for (var ch = 0; ch < channelCount; ch++)
            {
                var sample = samples[(ch * sampleCount) + i];
                WriteSample(output, ref offset, sample, bytesPerSample);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteSample(Span<byte> output, ref int offset, int sample, int bytesPerSample)
    {
        switch (bytesPerSample)
        {
            case 1:
                output[offset++] = (byte)(sample + 128);
                break;
            case 2:
                BinaryPrimitives.WriteInt16LittleEndian(output.Slice(offset, 2), (short)sample);
                offset += 2;
                break;
            default:
                BinaryPrimitives.WriteInt32LittleEndian(output.Slice(offset, 4), sample);
                offset += 4;
                break;
        }
    }

    private int GetBytesPerSample()
    {
        if (bitDepth <= 8)
            return 1;

        return bitDepth <= 16 ? 2 : 4;
    }

    /// <summary>
    /// Читает знаковое N-битное значение (two's complement) из BitReader.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReadSigned(ref BitReader reader, int bits)
    {
        var value = reader.ReadBits(bits);
        var signBit = 1U << (bits - 1);

        if ((value & signBit) != 0)
            value |= ~((1U << bits) - 1);

        return (int)value;
    }

    #endregion
}
