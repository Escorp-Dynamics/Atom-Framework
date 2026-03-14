using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Atom.Media;

/// <summary>
/// Демуксер WAV-файлов (RIFF/WAVE).
/// Поддерживает PCM (int) и IEEE Float форматы.
/// </summary>
internal sealed class WavDemuxer : IDemuxer
{
    private const int RiffHeaderSize = 12;
    private const int MinFmtChunkSize = 16;
    private const int DefaultPacketSamples = 1024;

    private byte[] audioData = [];
    private bool isOpen;
    private bool isDisposed;

    private int BytesPerPacket { get; set; }
    private int ReadPosition { get; set; }
    private MediaStreamInfo StreamInfo { get; set; }

    /// <inheritdoc/>
    public ContainerInfo ContainerInfo { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<MediaStreamInfo> Streams => isOpen ? [StreamInfo] : [];

    /// <inheritdoc/>
    public int BestVideoStreamIndex => -1;

    /// <inheritdoc/>
    public int BestAudioStreamIndex => isOpen ? 0 : -1;

    /// <inheritdoc/>
    public ContainerResult Open(string path)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!File.Exists(path))
        {
            return ContainerResult.FileNotFound;
        }

        return ParseWav(File.ReadAllBytes(path));
    }

    /// <inheritdoc/>
    public ContainerResult Open(Stream stream)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(stream);

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ParseWav(ms.GetBuffer().AsSpan(0, (int)ms.Length));
    }

    /// <inheritdoc/>
    public ContainerResult ReadPacket(MediaPacketBuffer packet)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isOpen)
        {
            return ContainerResult.NotOpen;
        }

        if (ReadPosition >= audioData.Length)
        {
            return ContainerResult.EndOfFile;
        }

        var remaining = audioData.Length - ReadPosition;
        var size = Math.Min(BytesPerPacket, remaining);

        packet.SetData(audioData.AsSpan(ReadPosition, size));
        packet.StreamIndex = 0;

        var parameters = StreamInfo.AudioParameters!.Value;
        var bytesPerSample = parameters.SampleFormat.GetBytesPerSample();
        var channels = parameters.ChannelCount;
        var sampleRate = parameters.SampleRate;
        var sampleCount = size / (bytesPerSample * channels);
        var durationUs = (long)(sampleCount * 1_000_000.0 / sampleRate);
        var currentSample = ReadPosition / (bytesPerSample * channels);
        var ptsUs = (long)(currentSample * 1_000_000.0 / sampleRate);

        packet.PtsUs = ptsUs;
        packet.DtsUs = ptsUs;
        packet.DurationUs = durationUs;

        ReadPosition += size;

        return ContainerResult.Success;
    }

    /// <inheritdoc/>
    public ValueTask<ContainerResult> ReadPacketAsync(
        MediaPacketBuffer packet,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable MA0042
        return new ValueTask<ContainerResult>(ReadPacket(packet));
#pragma warning restore MA0042
    }

    /// <inheritdoc/>
    public ContainerResult Seek(TimeSpan timestamp, int streamIndex = -1, bool seekToKeyframe = true)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!isOpen)
        {
            return ContainerResult.NotOpen;
        }

        var parameters = StreamInfo.AudioParameters!.Value;
        var bytesPerSample = parameters.SampleFormat.GetBytesPerSample();
        var channels = parameters.ChannelCount;
        var sampleRate = parameters.SampleRate;

        var targetSample = (int)(timestamp.TotalSeconds * sampleRate);
        var frameBytes = bytesPerSample * channels;
        var byteOffset = targetSample * frameBytes;

        ReadPosition = Math.Clamp(byteOffset, 0, audioData.Length);

        return ContainerResult.Success;
    }

    /// <inheritdoc/>
    public void Reset() => ReadPosition = 0;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        audioData = [];
        isOpen = false;
        ReadPosition = 0;
    }

    private ContainerResult ParseWav(ReadOnlySpan<byte> data)
    {
        if (data.Length < 44)
        {
            return ContainerResult.CorruptData;
        }

        if (!IsRiffWave(data))
        {
            return ContainerResult.UnsupportedFormat;
        }

        var result = ScanChunks(data, out var fmt, out var pcmStart, out var pcmLen);
        if (result != ContainerResult.Success)
        {
            return result;
        }

        return BuildState(fmt, data.Slice(pcmStart, pcmLen));
    }

    private static ContainerResult ScanChunks(
        ReadOnlySpan<byte> data, out FmtInfo fmt, out int pcmStart, out int pcmLen)
    {
        fmt = default;
        pcmStart = 0;
        pcmLen = 0;
        var pos = RiffHeaderSize;
        var hasFmt = false;

        while (pos + 8 <= data.Length)
        {
            var chunkId = data.Slice(pos, 4);
            var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos + 4, 4));

            if (chunkId is [(byte)'f', (byte)'m', (byte)'t', (byte)' '])
            {
                if (chunkSize < MinFmtChunkSize)
                {
                    return ContainerResult.CorruptData;
                }

                fmt = ParseFmt(data, pos);
                hasFmt = true;
            }
            else if (chunkId is [(byte)'d', (byte)'a', (byte)'t', (byte)'a'])
            {
                if (!hasFmt)
                {
                    return ContainerResult.CorruptData;
                }

                pcmLen = Math.Min(chunkSize, data.Length - pos - 8);
                pcmStart = pos + 8;
                return ContainerResult.Success;
            }

            pos += 8 + chunkSize;
            if (chunkSize % 2 != 0)
            {
                pos++;
            }
        }

        return ContainerResult.CorruptData;
    }

    private ContainerResult BuildState(FmtInfo fmt, ReadOnlySpan<byte> pcmSpan)
    {
        var sampleFormat = MapSampleFormat(fmt.AudioFormat, fmt.BitsPerSample);
        if (sampleFormat == AudioSampleFormat.Unknown)
        {
            return ContainerResult.UnsupportedFormat;
        }

        audioData = pcmSpan.ToArray();
        ReadPosition = 0;

        var bytesPerSample = sampleFormat.GetBytesPerSample();
        var frameBytes = bytesPerSample * fmt.Channels;
        BytesPerPacket = DefaultPacketSamples * frameBytes;

        var totalSamples = audioData.Length / frameBytes;
        var durationUs = (long)(totalSamples * 1_000_000.0 / fmt.SampleRate);

        var codecId = MapCodecId(fmt.AudioFormat, fmt.BitsPerSample);

        var audioParams = new AudioCodecParameters
        {
            SampleRate = fmt.SampleRate,
            ChannelCount = fmt.Channels,
            SampleFormat = sampleFormat,
            BitRate = (long)fmt.SampleRate * fmt.Channels * fmt.BitsPerSample,
        };

        StreamInfo = new MediaStreamInfo
        {
            Index = 0,
            Type = MediaStreamType.Audio,
            CodecId = codecId,
            DurationUs = durationUs,
            BitRate = audioParams.BitRate,
            AudioParameters = audioParams,
        };

        ContainerInfo = new ContainerInfo
        {
            FormatName = "wav",
            DurationUs = durationUs,
            BitRate = audioParams.BitRate,
            FileSize = audioData.Length + 44,
            IsSeekable = true,
            IsLiveStream = false,
            StreamCount = 1,
        };

        isOpen = true;
        return ContainerResult.Success;
    }

    private static bool IsRiffWave(ReadOnlySpan<byte> data) =>
        data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'
        && data[8] == 'W' && data[9] == 'A' && data[10] == 'V' && data[11] == 'E';

    private static FmtInfo ParseFmt(ReadOnlySpan<byte> data, int pos) => new()
    {
        AudioFormat = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(pos + 8, 2)),
        Channels = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(pos + 10, 2)),
        SampleRate = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos + 12, 4)),
        BitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(pos + 22, 2)),
    };

    private static AudioSampleFormat MapSampleFormat(int audioFormat, int bitsPerSample) =>
        audioFormat switch
        {
            1 => bitsPerSample switch
            {
                8 => AudioSampleFormat.U8,
                16 => AudioSampleFormat.S16,
                32 => AudioSampleFormat.S32,
                _ => AudioSampleFormat.Unknown,
            },
            3 => bitsPerSample switch
            {
                32 => AudioSampleFormat.F32,
                64 => AudioSampleFormat.F64,
                _ => AudioSampleFormat.Unknown,
            },
            _ => AudioSampleFormat.Unknown,
        };

    private static MediaCodecId MapCodecId(int audioFormat, int bitsPerSample) =>
        audioFormat switch
        {
            1 => bitsPerSample switch
            {
                8 => MediaCodecId.PcmU8,
                16 => MediaCodecId.PcmS16Le,
                32 => MediaCodecId.PcmS32Le,
                _ => MediaCodecId.Unknown,
            },
            3 => bitsPerSample switch
            {
                32 => MediaCodecId.PcmF32Le,
                64 => MediaCodecId.PcmF64Le,
                _ => MediaCodecId.Unknown,
            },
            _ => MediaCodecId.Unknown,
        };

    [StructLayout(LayoutKind.Auto)]
    private struct FmtInfo
    {
        public int AudioFormat;
        public int Channels;
        public int SampleRate;
        public int BitsPerSample;
    }
}
