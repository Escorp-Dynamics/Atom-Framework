using System.Buffers.Binary;

namespace Atom.Media;

/// <summary>
/// Муксер WAV (RIFF/WAVE). Записывает PCM и IEEE Float аудио.
/// </summary>
internal sealed class WavMuxer : IMuxer
{
    private Stream? outputStream;
    private bool ownsStream;
    private bool isDisposed;
    private bool headerWritten;
    private long dataStartPosition;
    private long totalDataBytes;
    private AudioCodecParameters audioParams;

    /// <inheritdoc/>
    public bool IsOpen { get; private set; }

    /// <inheritdoc/>
    public int StreamCount { get; private set; }

    /// <inheritdoc/>
    public ContainerResult Open(in MuxerParameters parameters)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (parameters.OutputStream is not null)
        {
            outputStream = parameters.OutputStream;
            ownsStream = false;
        }
        else if (parameters.OutputPath is not null)
        {
            outputStream = File.Create(parameters.OutputPath);
            ownsStream = true;
        }
        else
        {
            return ContainerResult.Error;
        }

        IsOpen = true;
        return ContainerResult.Success;
    }

    /// <inheritdoc/>
    public (ContainerResult Result, int StreamIndex) AddVideoStream(
        in VideoCodecParameters parameters, MediaCodecId codecId) =>
        (ContainerResult.UnsupportedFormat, -1);

    /// <inheritdoc/>
    public (ContainerResult Result, int StreamIndex) AddAudioStream(
        in AudioCodecParameters parameters, MediaCodecId codecId)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!IsOpen || StreamCount > 0)
        {
            return (ContainerResult.Error, -1);
        }

        audioParams = parameters;
        StreamCount = 1;

        return (ContainerResult.Success, 0);
    }

    /// <inheritdoc/>
    public ContainerResult WriteHeader()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!IsOpen || StreamCount == 0 || outputStream is null)
        {
            return ContainerResult.Error;
        }

        var bitsPerSample = audioParams.SampleFormat.GetBytesPerSample() * 8;
        var blockAlign = audioParams.ChannelCount * (bitsPerSample / 8);
        var byteRate = audioParams.SampleRate * blockAlign;
        var audioFormat = IsFloatFormat(audioParams.SampleFormat) ? (ushort)3 : (ushort)1;

        Span<byte> header = stackalloc byte[44];

        // RIFF header
        header[0] = (byte)'R';
        header[1] = (byte)'I';
        header[2] = (byte)'F';
        header[3] = (byte)'F';
        // file size - 8 (placeholder, updated in WriteTrailer)
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], 0);
        header[8] = (byte)'W';
        header[9] = (byte)'A';
        header[10] = (byte)'V';
        header[11] = (byte)'E';

        // fmt chunk
        header[12] = (byte)'f';
        header[13] = (byte)'m';
        header[14] = (byte)'t';
        header[15] = (byte)' ';
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16); // chunk size
        BinaryPrimitives.WriteUInt16LittleEndian(header[20..], audioFormat);
        BinaryPrimitives.WriteUInt16LittleEndian(header[22..], (ushort)audioParams.ChannelCount);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], audioParams.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], byteRate);
        BinaryPrimitives.WriteUInt16LittleEndian(header[32..], (ushort)blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(header[34..], (ushort)bitsPerSample);

        // data chunk header
        header[36] = (byte)'d';
        header[37] = (byte)'a';
        header[38] = (byte)'t';
        header[39] = (byte)'a';
        // data size (placeholder, updated in WriteTrailer)
        BinaryPrimitives.WriteInt32LittleEndian(header[40..], 0);

        outputStream.Write(header);
        dataStartPosition = outputStream.Position;
        headerWritten = true;
        totalDataBytes = 0;

        return ContainerResult.Success;
    }

    /// <inheritdoc/>
    public ContainerResult WritePacket(in MediaPacket packet)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!headerWritten || outputStream is null)
        {
            return ContainerResult.Error;
        }

        outputStream.Write(packet.Data);
        totalDataBytes += packet.Data.Length;

        return ContainerResult.Success;
    }

    /// <inheritdoc/>
    public async ValueTask<ContainerResult> WritePacketAsync(
        MediaPacketBuffer packet,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!headerWritten || outputStream is null)
        {
            return ContainerResult.Error;
        }

        await outputStream.WriteAsync(packet.GetMemory(), cancellationToken).ConfigureAwait(false);
        totalDataBytes += packet.Size;

        return ContainerResult.Success;
    }

    /// <inheritdoc/>
    public ContainerResult WriteTrailer()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!headerWritten || outputStream is null)
        {
            return ContainerResult.Error;
        }

        if (outputStream.CanSeek)
        {
            FinalizeHeader();
        }

        outputStream.Flush();
        return ContainerResult.Success;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;

        if (ownsStream)
        {
            outputStream?.Dispose();
        }

        outputStream = null;
        IsOpen = false;
    }

    private void FinalizeHeader()
    {
        Span<byte> buf = stackalloc byte[4];

        // Update data chunk size
        outputStream!.Position = dataStartPosition - 4;
        BinaryPrimitives.WriteInt32LittleEndian(buf, (int)totalDataBytes);
        outputStream.Write(buf);

        // Update RIFF size = file size - 8
        outputStream.Position = 4;
        BinaryPrimitives.WriteInt32LittleEndian(buf, (int)(totalDataBytes + 36));
        outputStream.Write(buf);

        // Seek back to end
        outputStream.Seek(0, SeekOrigin.End);
    }

    private static bool IsFloatFormat(AudioSampleFormat fmt) =>
        fmt is AudioSampleFormat.F32 or AudioSampleFormat.F64;
}
