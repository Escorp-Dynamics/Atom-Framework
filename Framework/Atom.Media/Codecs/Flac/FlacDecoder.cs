#pragma warning disable CA1822, MA0042, IDE0032

using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Atom.IO;
using Microsoft.Extensions.Logging;

namespace Atom.Media;

/// <summary>
/// Кодек FLAC (Free Lossless Audio Codec).
/// Декодирование: все типы субфреймов (CONSTANT, VERBATIM, FIXED 0-4, LPC), Rice/Rice2.
/// Кодирование: CONSTANT / FIXED (0-4) с автовыбором порядка, Rice.
/// Каналы: independent, left/side, side/right, mid/side.
/// </summary>
public sealed class FlacDecoder : IAudioCodec
{
    private AudioCodecParameters parameters;
    private int bitsPerSample;
    private bool isInitialized;
    private bool isEncoder;
    private int frameNumber;
    private bool isDisposed;

    /// <inheritdoc/>
    public MediaCodecId CodecId => MediaCodecId.Flac;

    /// <inheritdoc/>
    public string Name => "FLAC Audio";

    /// <inheritdoc/>
    public CodecCapabilities Capabilities => CodecCapabilities.Decode | CodecCapabilities.Encode | CodecCapabilities.Lossless;

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
        bitsPerSample = parameters.SampleFormat switch
        {
            AudioSampleFormat.U8 => 8,
            AudioSampleFormat.S16 => 16,
            AudioSampleFormat.S32 => 32,
            _ => 16,
        };
        isInitialized = true;
        return CodecResult.Success;
    }

    /// <inheritdoc/>
    public CodecResult InitializeEncoder(in AudioCodecParameters parameters)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (parameters.SampleRate <= 0 || parameters.ChannelCount <= 0)
            return CodecResult.InvalidData;

        this.parameters = parameters;
        bitsPerSample = parameters.SampleFormat switch
        {
            AudioSampleFormat.U8 => 8,
            AudioSampleFormat.S16 => 16,
            AudioSampleFormat.S32 => 32,
            _ => 16,
        };
        isInitialized = true;
        isEncoder = true;
        frameNumber = 0;
        return CodecResult.Success;
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
            buffer.Allocate(4096, parameters.ChannelCount, parameters.SampleRate, parameters.SampleFormat);

        var frame = buffer.AsFrame();
        var result = Decode(packet.Span, ref frame);
        return new ValueTask<CodecResult>(result);
    }

    #endregion

    #region Encode

    /// <inheritdoc/>
    public CodecResult Encode(in ReadOnlyAudioFrame frame, Span<byte> output, out int bytesWritten)
    {
        bytesWritten = 0;
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isInitialized || !isEncoder)
            return CodecResult.NotInitialized;

        var sampleCount = frame.Info.SampleCount;
        var channelCount = frame.Info.ChannelCount;
        var bytesPerSample = GetBytesPerSampleFromBps(bitsPerSample);
        var minSize = 20 + (channelCount * sampleCount * bytesPerSample);

        if (output.Length < minSize)
            return CodecResult.OutputBufferTooSmall;

        var samples = ArrayPool<int>.Shared.Rent(sampleCount * channelCount);
        try
        {
            ReadSamplesFromFrame(in frame, samples, sampleCount, channelCount);
            return EncodeFlacFrame(samples, sampleCount, channelCount, output, out bytesWritten);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(samples);
        }
    }

    /// <inheritdoc/>
    public ValueTask<(CodecResult Result, int BytesWritten)> EncodeAsync(
        [NotNull] AudioFrameBuffer frame,
        Memory<byte> output,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isInitialized || !isEncoder)
            return new((CodecResult.NotInitialized, 0));

        cancellationToken.ThrowIfCancellationRequested();

        var roFrame = frame.AsReadOnlyFrame();
        var result = Encode(in roFrame, output.Span, out var written);
        return new((result, written));
    }

    #endregion

    #region Flush, Reset, Dispose

    /// <inheritdoc/>
    public CodecResult Flush(ref AudioFrame frame) =>
        CodecResult.EndOfStream;

    /// <inheritdoc/>
    public void Reset()
    {
        // FLAC декодер без состояния между фреймами
    }

    /// <inheritdoc/>
    public void Dispose() => isDisposed = true;

    #endregion

    #region Frame Decoding

    private CodecResult DecodeFrame(ReadOnlySpan<byte> packet, ref AudioFrame frame)
    {
        var reader = new BitReader(packet);

        var header = ParseFrameHeader(ref reader);
        if (header.BlockSize <= 0)
            return CodecResult.InvalidData;

        var channelCount = header.ChannelCount;
        var blockSize = header.BlockSize;
        var bps = header.BitsPerSample > 0 ? header.BitsPerSample : bitsPerSample;

        var totalInts = blockSize * channelCount;
        var rentedArray = ArrayPool<int>.Shared.Rent(totalInts);
        var samples = rentedArray.AsSpan(0, totalInts);
        samples.Clear();

        try
        {
            for (var ch = 0; ch < channelCount; ch++)
            {
                var subframeBps = GetSubframeBps(bps, header.ChannelAssignment, ch);
                DecodeSubframe(ref reader, samples.Slice(ch * blockSize, blockSize), blockSize, subframeBps);
            }

            if (channelCount == 2 && header.ChannelAssignment >= 8)
                ApplyChannelDecorrelation(samples[..blockSize], samples.Slice(blockSize, blockSize), header.ChannelAssignment);

            WriteSamplesToFrame(samples, blockSize, channelCount, bps, ref frame);
            return CodecResult.Success;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(rentedArray);
        }
    }

    #endregion

    #region Frame Header

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct FlacFrameHeader(
        int BlockSize,
        int SampleRate,
        int ChannelCount,
        int ChannelAssignment,
        int BitsPerSample);

    private static FlacFrameHeader ParseFrameHeader(ref BitReader reader)
    {
        var sync = reader.ReadBits(14);
        if (sync != 0x3FFE)
            return default;

        reader.SkipBits(1); // reserved
        reader.SkipBits(1); // blocking strategy

        var blockSizeCode = (int)reader.ReadBits(4);
        var sampleRateCode = (int)reader.ReadBits(4);
        var channelAssignment = (int)reader.ReadBits(4);
        var sampleSizeCode = (int)reader.ReadBits(3);
        reader.SkipBits(1); // reserved

        ReadUtf8Number(ref reader);

        var blockSize = DecodeBlockSizeValue(ref reader, blockSizeCode);
        var sampleRate = DecodeSampleRateValue(ref reader, sampleRateCode);

        if (channelAssignment > 10)
            return default;

        var channelCount = channelAssignment <= 7 ? channelAssignment + 1 : 2;
        var sampleBits = DecodeSampleSize(sampleSizeCode);

        reader.ReadBits(8); // CRC-8

        return new FlacFrameHeader(blockSize, sampleRate, channelCount, channelAssignment, sampleBits);
    }

    private static int DecodeBlockSizeValue(ref BitReader reader, int code) => code switch
    {
        1 => 192,
        2 or 3 or 4 or 5 => 576 << (code - 2),
        6 => (int)reader.ReadBits(8) + 1,
        7 => (int)reader.ReadBits(16) + 1,
        >= 8 and <= 15 => 256 << (code - 8),
        _ => 0,
    };

    private static int DecodeSampleRateValue(ref BitReader reader, int code) => code switch
    {
        1 => 88200,
        2 => 176400,
        3 => 192000,
        4 => 8000,
        5 => 16000,
        6 => 22050,
        7 => 24000,
        8 => 32000,
        9 => 44100,
        10 => 48000,
        11 => 96000,
        12 => (int)reader.ReadBits(8) * 1000,
        13 => (int)reader.ReadBits(16),
        14 => (int)reader.ReadBits(16) * 10,
        _ => 0,
    };

    private static int DecodeSampleSize(int code) => code switch
    {
        1 => 8,
        2 => 12,
        4 => 16,
        5 => 20,
        6 => 24,
        7 => 32,
        _ => 0,
    };

    private static void ReadUtf8Number(ref BitReader reader)
    {
        var first = (int)reader.ReadBits(8);

        if ((first & 0x80) == 0)
            return;

        var bytes = 1;
        var mask = 0x40;

        while ((first & mask) != 0)
        {
            bytes++;
            mask >>= 1;
        }

        for (var i = 1; i < bytes; i++)
            reader.ReadBits(8);
    }

    private static int GetSubframeBps(int bps, int channelAssignment, int channel) =>
        channelAssignment switch
        {
            8 when channel == 1 => bps + 1,
            9 when channel == 0 => bps + 1,
            10 when channel == 1 => bps + 1,
            _ => bps,
        };

    #endregion

    #region Subframe Decoding

    private static void DecodeSubframe(ref BitReader reader, Span<int> output, int blockSize, int bps)
    {
        reader.SkipBits(1); // zero padding

        var typeCode = (int)reader.ReadBits(6);

        var wastedBits = 0;

        if (reader.ReadBit())
        {
            wastedBits = 1;

            while (!reader.ReadBit())
                wastedBits++;

            bps -= wastedBits;
        }

        DispatchSubframeType(ref reader, output, blockSize, bps, typeCode);

        if (wastedBits > 0)
        {
            for (var i = 0; i < blockSize; i++)
                output[i] <<= wastedBits;
        }
    }

    private static void DispatchSubframeType(ref BitReader reader, Span<int> output, int blockSize, int bps, int typeCode)
    {
        if (typeCode == 0)
            DecodeConstant(ref reader, output, blockSize, bps);
        else if (typeCode == 1)
            DecodeVerbatim(ref reader, output, blockSize, bps);
        else if (typeCode is >= 8 and <= 12)
            DecodeFixed(ref reader, output, blockSize, bps, typeCode - 8);
        else if (typeCode >= 32)
            DecodeLpc(ref reader, output, blockSize, bps, typeCode - 31);
        else
            output[..blockSize].Clear();
    }

    private static void DecodeConstant(ref BitReader reader, Span<int> output, int blockSize, int bps)
    {
        var value = ReadSigned(ref reader, bps);
        output[..blockSize].Fill(value);
    }

    private static void DecodeVerbatim(ref BitReader reader, Span<int> output, int blockSize, int bps)
    {
        for (var i = 0; i < blockSize; i++)
            output[i] = ReadSigned(ref reader, bps);
    }

    #endregion

    #region Fixed Prediction

    private static void DecodeFixed(ref BitReader reader, Span<int> output, int blockSize, int bps, int order)
    {
        for (var i = 0; i < order; i++)
            output[i] = ReadSigned(ref reader, bps);

        DecodeResidual(ref reader, output, blockSize, order);
        RestoreFixedPrediction(output, blockSize, order);
    }

    private static void RestoreFixedPrediction(Span<int> output, int blockSize, int order)
    {
        switch (order)
        {
            case 1:
                for (var i = 1; i < blockSize; i++)
                    output[i] += output[i - 1];
                break;
            case 2:
                for (var i = 2; i < blockSize; i++)
                    output[i] += (2 * output[i - 1]) - output[i - 2];
                break;
            case 3:
                for (var i = 3; i < blockSize; i++)
                    output[i] += (3 * output[i - 1]) - (3 * output[i - 2]) + output[i - 3];
                break;
            case 4:
                for (var i = 4; i < blockSize; i++)
                    output[i] += (4 * output[i - 1]) - (6 * output[i - 2]) + (4 * output[i - 3]) - output[i - 4];
                break;
        }
    }

    #endregion

    #region LPC Prediction

    private static void DecodeLpc(ref BitReader reader, Span<int> output, int blockSize, int bps, int order)
    {
        for (var i = 0; i < order; i++)
            output[i] = ReadSigned(ref reader, bps);

        var precision = (int)reader.ReadBits(4) + 1;
        var shift = ReadSigned(ref reader, 5);

        Span<int> coefficients = stackalloc int[order];

        for (var i = 0; i < order; i++)
            coefficients[i] = ReadSigned(ref reader, precision);

        DecodeResidual(ref reader, output, blockSize, order);
        RestoreLpcPrediction(output, blockSize, order, coefficients, shift);
    }

    private static void RestoreLpcPrediction(Span<int> output, int blockSize, int order, Span<int> coefficients, int shift)
    {
        for (var i = order; i < blockSize; i++)
        {
            var prediction = 0L;

            for (var j = 0; j < order; j++)
                prediction += (long)coefficients[j] * output[i - j - 1];

            output[i] += (int)(prediction >> shift);
        }
    }

    #endregion

    #region Rice Entropy Decoding

    private static void DecodeResidual(ref BitReader reader, Span<int> output, int blockSize, int predictorOrder)
    {
        var method = (int)reader.ReadBits(2);
        var partitionOrder = (int)reader.ReadBits(4);
        var partitions = 1 << partitionOrder;
        var riceParamBits = method == 0 ? 4 : 5;
        var escapeValue = method == 0 ? 15 : 31;
        var sampleIndex = predictorOrder;

        for (var partition = 0; partition < partitions; partition++)
        {
            var samplesInPartition = partition == 0
                ? (blockSize >> partitionOrder) - predictorOrder
                : blockSize >> partitionOrder;

            var riceParam = (int)reader.ReadBits(riceParamBits);

            if (riceParam == escapeValue)
                DecodeEscapedPartition(ref reader, output, ref sampleIndex, samplesInPartition);
            else
                DecodeRiceSamples(ref reader, output, ref sampleIndex, samplesInPartition, riceParam);
        }
    }

    private static void DecodeEscapedPartition(ref BitReader reader, Span<int> output, ref int index, int count)
    {
        var rawBits = (int)reader.ReadBits(5);

        for (var i = 0; i < count; i++)
            output[index++] = ReadSigned(ref reader, rawBits);
    }

    private static void DecodeRiceSamples(ref BitReader reader, Span<int> output, ref int index, int count, int riceParam)
    {
        for (var i = 0; i < count; i++)
        {
            var q = 0;

            while (!reader.ReadBit())
                q++;

            var r = riceParam > 0 ? (int)reader.ReadBits(riceParam) : 0;
            var value = (q << riceParam) | r;

            output[index++] = (value & 1) != 0 ? -(value >> 1) - 1 : value >> 1;
        }
    }

    #endregion

    #region Channel Decorrelation

    private static void ApplyChannelDecorrelation(Span<int> ch0, Span<int> ch1, int channelAssignment)
    {
        switch (channelAssignment)
        {
            case 8: // left/side → right = left - side
                for (var i = 0; i < ch0.Length; i++)
                    ch1[i] = ch0[i] - ch1[i];
                break;
            case 9: // side/right → left = side + right
                for (var i = 0; i < ch0.Length; i++)
                    ch0[i] += ch1[i];
                break;
            case 10: // mid/side
                ApplyMidSideDecorrelation(ch0, ch1);
                break;
        }
    }

    private static void ApplyMidSideDecorrelation(Span<int> mid, Span<int> side)
    {
        for (var i = 0; i < mid.Length; i++)
        {
            var m = mid[i];
            var s = side[i];
            m = (m << 1) | (s & 1);
            mid[i] = (m + s) >> 1;
            side[i] = (m - s) >> 1;
        }
    }

    #endregion

    #region Output

    private static void WriteSamplesToFrame(
        Span<int> samples, int blockSize, int channelCount, int bps, ref AudioFrame frame)
    {
        var output = frame.InterleavedData;
        var bytesPerSample = GetBytesPerSampleFromBps(bps);

        var offset = 0;

        for (var i = 0; i < blockSize; i++)
        {
            for (var ch = 0; ch < channelCount; ch++)
            {
                var sample = samples[(ch * blockSize) + i];
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

    private static int GetBytesPerSampleFromBps(int bps)
    {
        if (bps <= 8)
            return 1;

        return bps <= 16 ? 2 : 4;
    }

    /// <summary>
    /// Читает знаковое N-битное значение из BitReader.
    /// Atom.IO.BitReader.ReadSignedBits бросает OverflowException в checked-контексте,
    /// поэтому расширяем знак сами (Atom.Media компилируется с unchecked).
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

    #region Frame Encoding

    private CodecResult EncodeFlacFrame(
        Span<int> samples, int sampleCount, int channelCount,
        Span<byte> output, out int bytesWritten)
    {
        var writer = new BitWriter(output);

        // Frame header
        writer.WriteBits(0x3FFE, 14); // sync
        writer.WriteBits(0, 1); // reserved
        writer.WriteBits(0, 1); // fixed block size

        var blockSizeCode = GetEncodeBlockSizeCode(sampleCount);
        writer.WriteBits((uint)blockSizeCode, 4);

        var sampleRateCode = GetEncodeSampleRateCode(parameters.SampleRate);
        writer.WriteBits((uint)sampleRateCode, 4);

        writer.WriteBits((uint)(channelCount - 1), 4); // independent channels
        writer.WriteBits((uint)GetEncodeSampleSizeCode(bitsPerSample), 3);
        writer.WriteBits(0, 1); // reserved

        WriteUtf8FrameNumber(ref writer, frameNumber);

        if (blockSizeCode == 6)
            writer.WriteBits((uint)(sampleCount - 1), 8);
        else if (blockSizeCode == 7)
            writer.WriteBits((uint)(sampleCount - 1), 16);

        if (sampleRateCode == 12)
            writer.WriteBits((uint)(parameters.SampleRate / 1000), 8);
        else if (sampleRateCode == 13)
            writer.WriteBits((uint)parameters.SampleRate, 16);
        else if (sampleRateCode == 14)
            writer.WriteBits((uint)(parameters.SampleRate / 10), 16);

        // CRC-8 of header bytes
        var headerBytes = writer.BytesWritten;
        var crc8 = ComputeCrc8(output[..headerBytes]);
        writer.WriteBits(crc8, 8);

        // Subframes
        for (var ch = 0; ch < channelCount; ch++)
        {
            var channelSamples = samples.Slice(ch * sampleCount, sampleCount);
            EncodeSubframe(ref writer, channelSamples, sampleCount, bitsPerSample);
        }

        writer.AlignToByte();

        // CRC-16 of entire frame
        var frameBytes = writer.BytesWritten;
        var crc16 = ComputeCrc16(output[..frameBytes]);
        writer.WriteBits((uint)(crc16 >> 8), 8);
        writer.WriteBits((uint)(crc16 & 0xFF), 8);

        writer.Flush();
        bytesWritten = writer.BytesWritten;
        frameNumber++;
        return CodecResult.Success;
    }

    private static void EncodeSubframe(
        ref BitWriter writer, Span<int> samples, int sampleCount, int bps)
    {
        if (IsConstantBlock(samples, sampleCount))
        {
            EncodeConstantSubframe(ref writer, samples[0], bps);
            return;
        }

        var order = SelectBestFixedOrder(samples, sampleCount);
        EncodeFixedSubframe(ref writer, samples, sampleCount, bps, order);
    }

    private static void EncodeConstantSubframe(ref BitWriter writer, int value, int bps)
    {
        writer.WriteBits(0, 1); // padding
        writer.WriteBits(0, 6); // type = CONSTANT
        writer.WriteBits(0, 1); // no wasted bits
        WriteSigned(ref writer, value, bps);
    }

    private static void EncodeFixedSubframe(
        ref BitWriter writer, Span<int> samples, int sampleCount, int bps, int order)
    {
        writer.WriteBits(0, 1); // padding
        writer.WriteBits((uint)(8 + order), 6); // type = FIXED(order)
        writer.WriteBits(0, 1); // no wasted bits

        for (var i = 0; i < order; i++)
            WriteSigned(ref writer, samples[i], bps);

        var residualCount = sampleCount - order;
        var residuals = ArrayPool<int>.Shared.Rent(residualCount);
        try
        {
            ComputeFixedResiduals(samples, residuals, sampleCount, order);
            EncodeRiceResiduals(ref writer, residuals.AsSpan(0, residualCount), residualCount);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(residuals);
        }
    }

    private static void EncodeRiceResiduals(ref BitWriter writer, Span<int> residuals, int count)
    {
        writer.WriteBits(0, 2); // Rice method 0
        writer.WriteBits(0, 4); // partition order 0

        var k = ComputeRiceParameter(residuals, count);

        if (k > 14)
        {
            writer.WriteBits(15, 4); // escape
            var rawBits = BitsNeededForResiduals(residuals, count);
            writer.WriteBits((uint)rawBits, 5);

            for (var i = 0; i < count; i++)
                WriteSigned(ref writer, residuals[i], rawBits);

            return;
        }

        writer.WriteBits((uint)k, 4);

        for (var i = 0; i < count; i++)
        {
            var v = (long)residuals[i];
            var folded = (uint)(v >= 0 ? (2 * v) : ((-2 * v) - 1));
            var quotient = folded >> k;
            var remainder = folded & ((1U << k) - 1);

            for (var j = 0U; j < quotient; j++)
                writer.WriteBit(bit: false);

            writer.WriteBit(bit: true);

            if (k > 0)
                writer.WriteBits(remainder, k);
        }
    }

    private void ReadSamplesFromFrame(
        in ReadOnlyAudioFrame frame, Span<int> channelSamples, int sampleCount, int channelCount)
    {
        var data = frame.InterleavedData;
        var bytesPerSample = GetBytesPerSampleFromBps(bitsPerSample);

        for (var i = 0; i < sampleCount; i++)
        {
            for (var ch = 0; ch < channelCount; ch++)
            {
                var offset = ((i * channelCount) + ch) * bytesPerSample;
                channelSamples[(ch * sampleCount) + i] = bytesPerSample switch
                {
                    1 => data[offset] - 128,
                    2 => BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2)),
                    _ => BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4)),
                };
            }
        }
    }

    #endregion

    #region Encode Utilities

    private static bool IsConstantBlock(Span<int> samples, int count)
    {
        var first = samples[0];

        for (var i = 1; i < count; i++)
        {
            if (samples[i] != first)
                return false;
        }

        return true;
    }

    private static int SelectBestFixedOrder(Span<int> samples, int count)
    {
        if (count <= 4)
            return 0;

        var bestOrder = 0;
        var bestEnergy = ComputeResidualEnergy(samples, count, 0);

        for (var order = 1; order <= 4; order++)
        {
            var energy = ComputeResidualEnergy(samples, count, order);

            if (energy < bestEnergy)
            {
                bestEnergy = energy;
                bestOrder = order;
            }
        }

        return bestOrder;
    }

    private static long ComputeResidualEnergy(Span<int> samples, int count, int order)
    {
        long energy = 0;

        for (var i = order; i < count; i++)
        {
            var residual = (long)ComputeFixedPrediction(samples, i, order);
            energy += Math.Abs(residual);
        }

        return energy;
    }

    private static int ComputeFixedPrediction(Span<int> samples, int i, int order) =>
        order switch
        {
            0 => samples[i],
            1 => samples[i] - samples[i - 1],
            2 => samples[i] - (2 * samples[i - 1]) + samples[i - 2],
            3 => samples[i] - (3 * samples[i - 1]) + (3 * samples[i - 2]) - samples[i - 3],
            _ => samples[i] - (4 * samples[i - 1]) + (6 * samples[i - 2]) - (4 * samples[i - 3]) + samples[i - 4],
        };

    private static void ComputeFixedResiduals(Span<int> samples, Span<int> residuals, int count, int order)
    {
        for (var i = order; i < count; i++)
            residuals[i - order] = ComputeFixedPrediction(samples, i, order);
    }

    private static int ComputeRiceParameter(Span<int> residuals, int count)
    {
        if (count == 0)
            return 0;

        long sum = 0;

        for (var i = 0; i < count; i++)
        {
            var v = (long)residuals[i];
            sum += v >= 0 ? (2 * v) : ((-2 * v) - 1);
        }

        if (sum < count)
            return 0;

        var mean = sum / count;
        var k = 0;

        while ((1L << k) < mean && k < 15)
            k++;

        return Math.Min(14, k);
    }

    private static int BitsNeededForResiduals(Span<int> residuals, int count)
    {
        var maxAbs = 0L;

        for (var i = 0; i < count; i++)
            maxAbs = Math.Max(maxAbs, Math.Abs((long)residuals[i]));

        if (maxAbs == 0)
            return 1;

        var bits = 1;

        while ((1L << (bits - 1)) <= maxAbs)
            bits++;

        return Math.Min(32, bits);
    }

    /// <summary>
    /// Записывает знаковое N-битное значение в BitWriter.
    /// Аналог <see cref="ReadSigned"/> для кодирования.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteSigned(ref BitWriter writer, int value, int bits)
    {
        if (bits >= 32)
        {
            writer.WriteBits((uint)value, 32);
            return;
        }

        writer.WriteBits((uint)value & ((1U << bits) - 1), bits);
    }

    private static void WriteUtf8FrameNumber(ref BitWriter writer, int number)
    {
        if (number < 0x80)
        {
            writer.WriteBits((uint)number, 8);
        }
        else if (number < 0x800)
        {
            writer.WriteBits(0xC0 | ((uint)number >> 6), 8);
            writer.WriteBits(0x80 | ((uint)number & 0x3F), 8);
        }
        else
        {
            writer.WriteBits(0xE0 | ((uint)number >> 12), 8);
            writer.WriteBits(0x80 | (((uint)number >> 6) & 0x3F), 8);
            writer.WriteBits(0x80 | ((uint)number & 0x3F), 8);
        }
    }

    private static int GetEncodeBlockSizeCode(int blockSize) =>
        blockSize switch
        {
            192 => 1,
            576 => 2,
            1152 => 3,
            2304 => 4,
            4608 => 5,
            256 => 8,
            512 => 9,
            1024 => 10,
            2048 => 11,
            4096 => 12,
            8192 => 13,
            16384 => 14,
            32768 => 15,
            <= 256 => 6,
            _ => 7,
        };

    private static int GetEncodeSampleRateCode(int sampleRate) =>
        sampleRate switch
        {
            88200 => 1,
            176400 => 2,
            192000 => 3,
            8000 => 4,
            16000 => 5,
            22050 => 6,
            24000 => 7,
            32000 => 8,
            44100 => 9,
            48000 => 10,
            96000 => 11,
            _ when sampleRate % 1000 == 0 && sampleRate / 1000 <= 255 => 12,
            _ when sampleRate <= 65535 => 13,
            _ => 14,
        };

    private static int GetEncodeSampleSizeCode(int bps) =>
        bps switch
        {
            8 => 1,
            12 => 2,
            16 => 4,
            20 => 5,
            24 => 6,
            _ => 0,
        };

    private static byte ComputeCrc8(ReadOnlySpan<byte> data)
    {
        byte crc = 0;

        for (var i = 0; i < data.Length; i++)
        {
            crc ^= data[i];

            for (var bit = 0; bit < 8; bit++)
                crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1);
        }

        return crc;
    }

    private static ushort ComputeCrc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;

        for (var i = 0; i < data.Length; i++)
        {
            crc ^= (ushort)(data[i] << 8);

            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x8005 : crc << 1);
        }

        return crc;
    }

    #endregion
}
