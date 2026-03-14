using System.Buffers.Binary;

namespace Atom.Media;

/// <summary>
/// Муксер Ogg-контейнера (Vorbis/Opus).
/// </summary>
internal sealed class OggMuxer : IMuxer
{
    private const int MaxSegmentSize = 255;

    private Stream? outputStream;
    private bool ownsStream;
    private bool isDisposed;
    private bool headerWritten;
    private int serialNumber;
    private int pageSequence;
    private long granulePosition;
    private int sampleRate;
    private MediaCodecId codecId;

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

        serialNumber = System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue);
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

        if (codecId is not (MediaCodecId.Opus or MediaCodecId.Vorbis))
        {
            return (ContainerResult.UnsupportedFormat, -1);
        }

        sampleRate = parameters.SampleRate;
        this.codecId = codecId;
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

        if (codecId == MediaCodecId.Opus)
        {
            WriteOpusHeaders();
        }
        else
        {
            WriteVorbisHeaders();
        }

        headerWritten = true;
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

        var samplesInPacket = packet.DurationUs > 0 && sampleRate > 0
            ? (int)(packet.DurationUs * sampleRate / 1_000_000)
            : 960;
        granulePosition += samplesInPacket;

        WritePage(packet.Data, 0x00, granulePosition);
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

        var result = WritePacket(packet.AsPacket());
        await Task.CompletedTask.ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc/>
    public ContainerResult WriteTrailer()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!headerWritten || outputStream is null)
        {
            return ContainerResult.Error;
        }

        // Write EOS page (empty page with EOS flag)
        WritePage([], 0x04, granulePosition);
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

    private void WriteOpusHeaders()
    {
        // OpusHead
        using var head = new MemoryStream();
        head.Write("OpusHead"u8);
        head.WriteByte(1); // version
        head.WriteByte(2); // channels (default stereo)
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(buf, 0); // pre-skip
        head.Write(buf[..2]);
        BinaryPrimitives.WriteInt32LittleEndian(buf, sampleRate);
        head.Write(buf);
        BinaryPrimitives.WriteInt16LittleEndian(buf, 0); // output gain
        head.Write(buf[..2]);
        head.WriteByte(0); // channel mapping

        WritePage(head.ToArray(), 0x02, 0); // BOS

        // OpusTags
        using var tags = new MemoryStream();
        tags.Write("OpusTags"u8);
        var vendor = "Atom"u8;
        BinaryPrimitives.WriteInt32LittleEndian(buf, vendor.Length);
        tags.Write(buf);
        tags.Write(vendor);
        BinaryPrimitives.WriteInt32LittleEndian(buf, 0); // comment count
        tags.Write(buf);

        WritePage(tags.ToArray(), 0x00, 0);
    }

    private void WriteVorbisHeaders()
    {
        // Minimal Vorbis identification header
        using var ident = new MemoryStream();
        ident.WriteByte(0x01); // packet type
        ident.Write("vorbis"u8);
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, 0); // version
        ident.Write(buf);
        ident.WriteByte(2); // channels
        BinaryPrimitives.WriteInt32LittleEndian(buf, sampleRate);
        ident.Write(buf);
        BinaryPrimitives.WriteInt32LittleEndian(buf, 0); // bitrate max
        ident.Write(buf);
        BinaryPrimitives.WriteInt32LittleEndian(buf, 128000); // bitrate nominal
        ident.Write(buf);
        BinaryPrimitives.WriteInt32LittleEndian(buf, 0); // bitrate min
        ident.Write(buf);
        ident.WriteByte(0x88); // block sizes (256/2048)
        ident.WriteByte(1); // framing

        WritePage(ident.ToArray(), 0x02, 0); // BOS

        // Minimal Vorbis comment header
        using var comment = new MemoryStream();
        comment.WriteByte(0x03); // packet type
        comment.Write("vorbis"u8);
        BinaryPrimitives.WriteInt32LittleEndian(buf, 4); // vendor length
        comment.Write(buf);
        comment.Write("Atom"u8);
        BinaryPrimitives.WriteInt32LittleEndian(buf, 0); // comment count
        comment.Write(buf);
        comment.WriteByte(1); // framing

        // Minimal Vorbis setup header
        using var setup = new MemoryStream();
        setup.WriteByte(0x05); // packet type
        setup.Write("vorbis"u8);
        setup.Write(new byte[16]); // padding

        WritePage(comment.ToArray(), 0x00, 0);
        WritePage(setup.ToArray(), 0x00, 0);
    }

    private void WritePage(ReadOnlySpan<byte> data, byte headerType, long granule)
    {
        if (outputStream is null)
        {
            return;
        }

        var segmentCount = data.Length == 0
            ? 1
            : (data.Length + MaxSegmentSize - 1) / MaxSegmentSize;

        // Build segment table
        Span<byte> segments = stackalloc byte[segmentCount];
        var remaining = data.Length;

        for (var i = 0; i < segmentCount; i++)
        {
            if (remaining >= MaxSegmentSize)
            {
                segments[i] = MaxSegmentSize;
                remaining -= MaxSegmentSize;
            }
            else
            {
                segments[i] = (byte)remaining;
                remaining = 0;
            }
        }

        // Write page header (27 bytes + segment table)
        Span<byte> header = stackalloc byte[27];
        header[0] = (byte)'O';
        header[1] = (byte)'g';
        header[2] = (byte)'g';
        header[3] = (byte)'S';
        header[4] = 0; // version
        header[5] = headerType;
        BinaryPrimitives.WriteInt64LittleEndian(header[6..], granule);
        BinaryPrimitives.WriteInt32LittleEndian(header[14..], serialNumber);
        BinaryPrimitives.WriteInt32LittleEndian(header[18..], pageSequence);
        BinaryPrimitives.WriteInt32LittleEndian(header[22..], 0); // CRC placeholder
        header[26] = (byte)segmentCount;

        outputStream.Write(header);
        outputStream.Write(segments);
        outputStream.Write(data);

        pageSequence++;
    }
}
