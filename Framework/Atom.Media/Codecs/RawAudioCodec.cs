#pragma warning disable CA1822, IDE0032

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace Atom.Media;

/// <summary>
/// Кодек для raw (несжатых) аудио форматов.
/// Поддерживает S16, S32, F32 и другие PCM форматы.
/// </summary>
/// <remarks>
/// Этот кодек просто копирует данные между буферами без какого-либо
/// сжатия или преобразования. Полезен для тестирования пайплайна
/// и работы с уже декодированными данными.
/// </remarks>
public sealed class RawAudioCodec : IAudioCodec
{
    private AudioCodecParameters parameters;
    private bool isInitialized;
    private bool isEncoder;
    private bool isDisposed;

    /// <inheritdoc/>
    public MediaCodecId CodecId { get; private set; } = MediaCodecId.Unknown;

    /// <inheritdoc/>
    public string Name => "Raw PCM Audio";

    /// <inheritdoc/>
    public CodecCapabilities Capabilities => CodecCapabilities.Decode | CodecCapabilities.Encode;

    /// <inheritdoc/>
    public ILogger? Logger { get; set; }

    /// <inheritdoc/>
    public IMeterFactory? MeterFactory { get; set; }

    /// <inheritdoc/>
    public HardwareAcceleration Acceleration { get; init; } = HardwareAcceleration.Auto;

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
        CodecId = MapSampleFormatToCodecId(parameters.SampleFormat);
        isEncoder = false;
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
        CodecId = MapSampleFormatToCodecId(parameters.SampleFormat);
        isEncoder = true;
        isInitialized = true;

        return CodecResult.Success;
    }

    #endregion

    #region Decode

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CodecResult Decode(ReadOnlySpan<byte> packet, ref AudioFrame frame)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isInitialized)
            return CodecResult.NotInitialized;

        if (isEncoder)
            return CodecResult.UnsupportedFormat;

        var bytesPerSample = parameters.SampleFormat.GetBytesPerSample();
        var isPlanar = parameters.SampleFormat.IsPlanar();

        if (isPlanar)
        {
            // Planar: каждый канал в отдельной плоскости
            var samplesPerChannel = packet.Length / (bytesPerSample * parameters.ChannelCount);
            var channelSize = samplesPerChannel * bytesPerSample;

            for (var ch = 0; ch < parameters.ChannelCount && ch < 8; ch++)
            {
                var sourceOffset = ch * channelSize;
                var channelData = frame.GetChannel(ch);

                if (channelData.Length < channelSize)
                    return CodecResult.OutputBufferTooSmall;

                packet.Slice(sourceOffset, channelSize).CopyTo(channelData);
            }
        }
        else
        {
            // Interleaved: все данные в одной плоскости
            var destination = frame.InterleavedData;

            if (destination.Length < packet.Length)
                return CodecResult.OutputBufferTooSmall;

            packet.CopyTo(destination);
        }

        return CodecResult.Success;
    }

    /// <inheritdoc/>
    public async ValueTask<CodecResult> DecodeAsync(
        ReadOnlyMemory<byte> packet,
        [NotNull] AudioFrameBuffer buffer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isInitialized)
            return CodecResult.NotInitialized;

        if (isEncoder)
            return CodecResult.UnsupportedFormat;

        cancellationToken.ThrowIfCancellationRequested();

        var bytesPerSample = parameters.SampleFormat.GetBytesPerSample();
        var sampleCount = packet.Length / (bytesPerSample * parameters.ChannelCount);

        // Убеждаемся, что буфер выделен
        if (!buffer.IsAllocated)
            buffer.Allocate(sampleCount, parameters.ChannelCount, parameters.SampleRate, parameters.SampleFormat);

        // Для больших буферов — асинхронная копия
        if (packet.Length > 256 * 1024) // > 256KB
        {
            await Task.Run(() =>
            {
                var frame = buffer.AsFrame();
                CopyToFrame(packet.Span, ref frame, parameters.SampleFormat, parameters.ChannelCount);
            }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var frame = buffer.AsFrame();
            CopyToFrame(packet.Span, ref frame, parameters.SampleFormat, parameters.ChannelCount);
        }

        return CodecResult.Success;
    }

    #endregion

    #region Encode

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CodecResult Encode(in ReadOnlyAudioFrame frame, Span<byte> output, out int bytesWritten)
    {
        bytesWritten = 0;

        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isInitialized)
            return CodecResult.NotInitialized;

        if (!isEncoder)
            return CodecResult.UnsupportedFormat;

        var bytesPerSample = frame.Info.SampleFormat.GetBytesPerSample();
        var isPlanar = frame.Info.SampleFormat.IsPlanar();

        if (isPlanar)
        {
            // Planar: копируем каналы последовательно
            var channelSize = frame.SampleCount * bytesPerSample;
            var expectedSize = channelSize * frame.ChannelCount;

            if (output.Length < expectedSize)
                return CodecResult.OutputBufferTooSmall;

            var offset = 0;
            for (var ch = 0; ch < frame.ChannelCount && ch < 8; ch++)
            {
                var channelData = frame.GetChannel(ch);
                channelData[..channelSize].CopyTo(output[offset..]);
                offset += channelSize;
            }

            bytesWritten = offset;
        }
        else
        {
            // Interleaved: одна копия
            var interleavedData = frame.InterleavedData;
            var dataSize = frame.SampleCount * frame.ChannelCount * bytesPerSample;

            if (output.Length < dataSize)
                return CodecResult.OutputBufferTooSmall;

            interleavedData[..dataSize].CopyTo(output);
            bytesWritten = dataSize;
        }

        return CodecResult.Success;
    }

    /// <inheritdoc/>
    public async ValueTask<(CodecResult Result, int BytesWritten)> EncodeAsync(
        [NotNull] AudioFrameBuffer frame,
        Memory<byte> output,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isInitialized)
            return (CodecResult.NotInitialized, 0);

        if (!isEncoder)
            return (CodecResult.UnsupportedFormat, 0);

        cancellationToken.ThrowIfCancellationRequested();

        var bytesPerSample = frame.SampleFormat.GetBytesPerSample();
        var expectedSize = frame.SampleCount * frame.ChannelCount * bytesPerSample;

        if (output.Length < expectedSize)
            return (CodecResult.OutputBufferTooSmall, 0);

        // Для больших буферов — асинхронная копия
        if (expectedSize > 256 * 1024)
        {
            var bytesWritten = await Task.Run(() =>
            {
                var roFrame = frame.AsReadOnlyFrame();
                return CopyFrameToOutput(roFrame, output.Span);
            }, cancellationToken).ConfigureAwait(false);

            return (CodecResult.Success, bytesWritten);
        }
        else
        {
            var roFrame = frame.AsReadOnlyFrame();
            var bytesWritten = CopyFrameToOutput(roFrame, output.Span);
            return (CodecResult.Success, bytesWritten);
        }
    }

    #endregion

    #region Flush & Reset

    /// <inheritdoc/>
    public CodecResult Flush(ref AudioFrame frame) =>
        // Raw кодек не буферизует данные
        CodecResult.EndOfStream;

    /// <inheritdoc/>
    public void Reset()
    {
        // Raw кодек без состояния
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Вычисляет размер аудио данных в байтах.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CalculateFrameSize(int sampleCount, int channelCount, AudioSampleFormat format)
        => sampleCount * channelCount * format.GetBytesPerSample();

    /// <summary>
    /// Маппинг формата семплов на ID кодека.
    /// </summary>
    private static MediaCodecId MapSampleFormatToCodecId(AudioSampleFormat format) => format switch
    {
        AudioSampleFormat.S16 or AudioSampleFormat.S16Planar => MediaCodecId.PcmS16Le,
        AudioSampleFormat.S32 or AudioSampleFormat.S32Planar => MediaCodecId.PcmS32Le,
        AudioSampleFormat.F32 or AudioSampleFormat.F32Planar => MediaCodecId.PcmF32Le,
        AudioSampleFormat.F64 or AudioSampleFormat.F64Planar => MediaCodecId.PcmF64Le,
        AudioSampleFormat.U8 or AudioSampleFormat.U8Planar => MediaCodecId.PcmU8,
        _ => MediaCodecId.Unknown,
    };

    /// <summary>
    /// Копирует данные в frame.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopyToFrame(ReadOnlySpan<byte> source, ref AudioFrame frame, AudioSampleFormat format, int channelCount)
    {
        if (format.IsPlanar())
        {
            var channelSize = source.Length / channelCount;

            for (var ch = 0; ch < channelCount && ch < 8; ch++)
            {
                var srcOffset = ch * channelSize;
                source.Slice(srcOffset, channelSize).CopyTo(frame.GetChannel(ch));
            }
        }
        else
        {
            source.CopyTo(frame.InterleavedData);
        }
    }

    /// <summary>
    /// Копирует frame в output буфер.
    /// </summary>
    private static int CopyFrameToOutput(in ReadOnlyAudioFrame frame, Span<byte> output)
    {
        var bytesPerSample = frame.Info.SampleFormat.GetBytesPerSample();

        if (frame.Info.SampleFormat.IsPlanar())
        {
            var channelSize = frame.SampleCount * bytesPerSample;
            var offset = 0;

            for (var ch = 0; ch < frame.ChannelCount && ch < 8; ch++)
            {
                var channelData = frame.GetChannel(ch);
                channelData[..channelSize].CopyTo(output[offset..]);
                offset += channelSize;
            }

            return offset;
        }

        var dataSize = frame.SampleCount * frame.ChannelCount * bytesPerSample;
        frame.InterleavedData[..dataSize].CopyTo(output);
        return dataSize;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        isInitialized = false;
    }

    #endregion
}
