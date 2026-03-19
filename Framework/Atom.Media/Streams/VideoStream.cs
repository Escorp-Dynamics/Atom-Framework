using System.Runtime.CompilerServices;

namespace Atom.Media;

/// <summary>
/// Runtime-видеопоток, инкапсулирующий декодированный кадр и управление его выдачей.
/// </summary>
public sealed class VideoStream : MediaStream
{
    private readonly VideoFrameBuffer frameBuffer;
    private byte[]? packedFrameBuffer;
    private bool isPackedFrameDirty = true;
    private int readOffset;
    private IDemuxer? demuxer;
    private IVideoCodec? codec;
    private MediaPacketBuffer? packetBuffer;
    private int videoStreamIndex = -1;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VideoStream"/>.
    /// </summary>
    /// <param name="parameters">Параметры видеопотока.</param>
    /// <param name="codecId">Исходный codec id, если он уже известен.</param>
    public VideoStream(in VideoCodecParameters parameters, MediaCodecId codecId = MediaCodecId.Unknown)
        : base(MediaStreamType.Video)
    {
        ValidateParameters(parameters);

        CodecId = codecId;
        Parameters = parameters;
        frameBuffer = new VideoFrameBuffer(parameters.Width, parameters.Height, parameters.PixelFormat);
    }

    /// <summary>
    /// Текущие параметры видеопотока.
    /// </summary>
    public VideoCodecParameters Parameters { get; private set; }

    /// <summary>
    /// Codec ID текущего источника кадров.
    /// </summary>
    public MediaCodecId CodecId { get; private set; }

    /// <summary>
    /// Ширина кадра.
    /// </summary>
    public int Width
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Parameters.Width;
    }

    /// <summary>
    /// Высота кадра.
    /// </summary>
    public int Height
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Parameters.Height;
    }

    /// <summary>
    /// Pixel format текущего кадра.
    /// </summary>
    public VideoPixelFormat PixelFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Parameters.PixelFormat;
    }

    /// <summary>
    /// Частота кадров потока.
    /// </summary>
    public double FrameRate
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Parameters.FrameRate;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Parameters = Parameters with { FrameRate = value };
    }

    /// <summary>
    /// Указывает, содержит ли поток уже декодированный кадр.
    /// </summary>
    public bool HasFrame { get; private set; }

    /// <summary>
    /// Текущий декодированный кадр в сыром виде.
    /// </summary>
    public ReadOnlyMemory<byte> CurrentFrame
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (!HasFrame)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            EnsurePackedFrame();
            return packedFrameBuffer!.AsMemory(0, PackedFrameSize);
        }
    }

    /// <summary>
    /// Текущий кадр как stride-aware frame view.
    /// </summary>
    public ReadOnlyVideoFrame CurrentVideoFrame
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => HasFrame ? frameBuffer.AsReadOnlyFrame() : default;
    }

    /// <summary>
    /// Необязательная пост-обработка кадра сразу после декодирования.
    /// </summary>
    public Action<VideoFrameBuffer>? FrameTransform { get; set; }

    /// <summary>
    /// Указывает, достигнут ли конец выдачи текущего кадра.
    /// </summary>
    public override bool EndOfStream
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !HasFrame || (!IsLooped && readOffset >= PackedFrameSize);
    }

    /// <summary>
    /// Создаёт <see cref="VideoStream"/> из одиночного изображения.
    /// </summary>
    public static VideoStream FromStillImage(
        ReadOnlySpan<byte> data,
        string format,
        in VideoCodecParameters parameters)
    {
        var stream = new VideoStream(parameters);
        stream.LoadStillImage(data, format);
        return stream;
    }

    /// <summary>
    /// Создаёт <see cref="VideoStream"/> поверх уже открытого demuxer и берёт его во владение.
    /// </summary>
    public static VideoStream OpenDemuxer(IDemuxer demuxer, in VideoCodecParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(demuxer);

        var stream = new VideoStream(parameters);

        try
        {
            stream.AttachDemuxer(demuxer);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Обновляет параметры потока и при необходимости перераспределяет буфер кадра.
    /// </summary>
    public void Configure(in VideoCodecParameters parameters)
    {
        ThrowIfDisposed();
        ValidateParameters(parameters);

        if (parameters.Width != Width
            || parameters.Height != Height
            || parameters.PixelFormat != PixelFormat)
        {
            frameBuffer.Allocate(parameters.Width, parameters.Height, parameters.PixelFormat);
            packedFrameBuffer = null;
        }

        Parameters = parameters;
        isPackedFrameDirty = true;
        readOffset = 0;
    }

    /// <summary>
    /// Декодирует одиночное изображение в текущий кадр видеопотока.
    /// </summary>
    public void LoadStillImage(ReadOnlySpan<byte> data, string format)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(format);

        using var imageCodec = CodecRegistry.CreateImageCodec(format)
            ?? throw new NotSupportedException($"Формат изображения '{format}' не поддерживается.");

        imageCodec.InitializeDecoder(new ImageCodecParameters(Width, Height, PixelFormat))
            .ThrowIfError("Не удалось инициализировать image decoder.");

        var frame = frameBuffer.AsFrame();
        imageCodec.Decode(data, ref frame)
            .ThrowIfError("Не удалось декодировать изображение в VideoStream.");

        FrameTransform?.Invoke(frameBuffer);

        CodecId = CodecRegistry.GetImageCodecId(format);
        HasFrame = true;
        isPackedFrameDirty = true;
        readOffset = 0;
    }

    /// <summary>
    /// Читает и декодирует следующий кадр из привязанного контейнерного источника.
    /// </summary>
    public async ValueTask<bool> ReadNextFrameAsync(bool loop = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ThrowIfStreamingSourceIsMissing();

        while (!cancellationToken.IsCancellationRequested)
        {
            var readResult = await demuxer!.ReadPacketAsync(packetBuffer!, cancellationToken).ConfigureAwait(false);
            if (readResult == ContainerResult.EndOfFile)
            {
                if (loop)
                {
                    demuxer.Reset();
                    continue;
                }

                return false;
            }

            if (readResult != ContainerResult.Success || packetBuffer!.StreamIndex != videoStreamIndex)
            {
                continue;
            }

            var decodeResult = await codec!.DecodeAsync(
                packetBuffer.GetMemory(), frameBuffer, cancellationToken).ConfigureAwait(false);
            if (decodeResult != CodecResult.Success)
            {
                continue;
            }

            HasFrame = true;
            isPackedFrameDirty = true;
            readOffset = 0;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Декодирует поток и передаёт каждый кадр в указанный sink.
    /// </summary>
    public async Task StreamToAsync(
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> frameWriter,
        bool loop = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frameWriter);
        ThrowIfDisposed();
        ThrowIfStreamingSourceIsMissing();

        var effectiveFrameRate = FrameRate > 0 ? FrameRate : 30;
        var frameInterval = TimeSpan.FromSeconds(1.0 / effectiveFrameRate);

        while (await ReadNextFrameAsync(loop, cancellationToken).ConfigureAwait(false))
        {
            await frameWriter(CurrentFrame, cancellationToken).ConfigureAwait(false);
            await Task.Delay(frameInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Копирует текущий кадр в произвольный байтовый буфер.
    /// </summary>
    public void CopyCurrentFrameTo(Span<byte> destination)
    {
        ThrowIfDisposed();

        if (!HasFrame)
        {
            throw new InvalidOperationException("В VideoStream ещё нет декодированного кадра.");
        }

        var source = CurrentFrame.Span;
        if (destination.Length < source.Length)
        {
            throw new ArgumentException("Буфер назначения меньше текущего кадра.", nameof(destination));
        }

        source.CopyTo(destination);
    }

    /// <summary>
    /// Копирует текущий кадр в <see cref="VideoFrameBuffer"/>.
    /// </summary>
    public void CopyCurrentFrameTo(VideoFrameBuffer destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ThrowIfDisposed();

        if (!HasFrame)
        {
            throw new InvalidOperationException("В VideoStream ещё нет декодированного кадра.");
        }

        destination.CopyFrom(frameBuffer.AsFrame());
    }

    /// <summary>
    /// Асинхронно копирует текущий кадр в <see cref="VideoFrameBuffer"/>.
    /// </summary>
    public ValueTask<bool> ReadFrameAsync(VideoFrameBuffer destination, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (!HasFrame)
        {
            return ValueTask.FromResult(false);
        }

        CopyCurrentFrameTo(destination);
        readOffset = 0;
        return ValueTask.FromResult(true);
    }

    /// <summary>
    /// Читает сырой кадр в указанный буфер.
    /// </summary>
    public override int Read(Span<byte> buffer)
    {
        ThrowIfDisposed();

        if (!HasFrame || buffer.IsEmpty)
        {
            return 0;
        }

        var source = CurrentFrame.Span;
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            if (readOffset >= source.Length)
            {
                if (!IsLooped)
                {
                    break;
                }

                readOffset = 0;
            }

            var remaining = source.Length - readOffset;
            var bytesToCopy = Math.Min(remaining, buffer.Length - totalRead);
            source.Slice(readOffset, bytesToCopy).CopyTo(buffer[totalRead..(totalRead + bytesToCopy)]);
            readOffset += bytesToCopy;
            totalRead += bytesToCopy;
        }

        return totalRead;
    }

    /// <summary>
    /// Асинхронно читает сырой кадр в указанный буфер.
    /// </summary>
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(buffer.Span));
    }

    /// <summary>
    /// Сбрасывает позицию чтения текущего кадра.
    /// </summary>
    public override void Reset()
    {
        ThrowIfDisposed();
        readOffset = 0;
    }

    /// <summary>
    /// Освобождает ресурсы потока и кадра.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            packetBuffer?.Dispose();
            packetBuffer = null;

            codec?.Dispose();
            codec = null;

            demuxer?.Dispose();
            demuxer = null;

            packedFrameBuffer = null;
            frameBuffer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void AttachDemuxer(IDemuxer demuxer)
    {
        ThrowIfDisposed();

        if (this.demuxer is not null)
        {
            throw new InvalidOperationException("VideoStream уже привязан к контейнерному источнику.");
        }

        var sourceStreamIndex = demuxer.BestVideoStreamIndex;
        if (sourceStreamIndex < 0)
        {
            throw new InvalidOperationException("Видеопоток не найден в контейнере.");
        }

        var streamInfo = demuxer.Streams[sourceStreamIndex];
        var videoCodec = CodecRegistry.CreateVideoCodec(streamInfo.CodecId)
            ?? throw new NotSupportedException($"Видеокодек {streamInfo.CodecId} не зарегистрирован.");

        var decoderParameters = streamInfo.VideoParameters
            ?? new VideoCodecParameters
            {
                Width = Width,
                Height = Height,
                PixelFormat = PixelFormat,
                FrameRate = FrameRate,
            };

        videoCodec.InitializeDecoder(in decoderParameters)
            .ThrowIfError("Не удалось инициализировать видеодекодер в VideoStream.");

        CodecId = streamInfo.CodecId;
        Parameters = Parameters with
        {
            FrameRate = decoderParameters.FrameRate > 0 ? decoderParameters.FrameRate : Parameters.FrameRate,
        };

        this.demuxer = demuxer;
        codec = videoCodec;
        packetBuffer = new MediaPacketBuffer();
        videoStreamIndex = sourceStreamIndex;
    }

    private void ThrowIfStreamingSourceIsMissing()
    {
        if (demuxer is null || codec is null || packetBuffer is null || videoStreamIndex < 0)
        {
            throw new InvalidOperationException("VideoStream не привязан к контейнерному источнику.");
        }
    }

    private int PackedFrameSize
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => PixelFormat.CalculateFrameSize(Width, Height);
    }

    private void EnsurePackedFrame()
    {
        ThrowIfDisposed();

        var packedFrameSize = PackedFrameSize;
        if (packedFrameBuffer is null || packedFrameBuffer.Length < packedFrameSize)
        {
            packedFrameBuffer = new byte[packedFrameSize];
        }

        if (!isPackedFrameDirty)
        {
            return;
        }

        PackCurrentFrame(CurrentVideoFrame, packedFrameBuffer.AsSpan(0, packedFrameSize));
        isPackedFrameDirty = false;
    }

    private static void PackCurrentFrame(in ReadOnlyVideoFrame frame, Span<byte> destination)
    {
        if (frame.PixelFormat.GetPlaneCount() == 1)
        {
            CopyPlaneRows(frame.PackedData, destination);
            return;
        }

        var offset = 0;
        offset += CopyPlaneRows(frame.GetPlaneY(), destination[offset..]);

        switch (frame.PixelFormat)
        {
            case VideoPixelFormat.Nv12:
            case VideoPixelFormat.Nv21:
                CopyPlaneRows(frame.GetPlaneUV(), destination[offset..]);
                return;

            case VideoPixelFormat.Yuv420P:
            case VideoPixelFormat.Yuv422P:
            case VideoPixelFormat.Yuv444P:
                offset += CopyPlaneRows(frame.GetPlaneU(), destination[offset..]);
                CopyPlaneRows(frame.GetPlaneV(), destination[offset..]);
                return;

            default:
                throw new NotSupportedException($"Нормализация VideoStream для формата '{frame.PixelFormat}' пока не поддерживается.");
        }
    }

    private static int CopyPlaneRows(ReadOnlyPlane<byte> plane, Span<byte> destination)
    {
        var rowWidth = plane.Width;
        var totalSize = rowWidth * plane.Height;

        if (plane.Stride == rowWidth)
        {
            plane.Data[..totalSize].CopyTo(destination);
            return totalSize;
        }

        for (var y = 0; y < plane.Height; y++)
        {
            plane.GetRow(y).CopyTo(destination.Slice(y * rowWidth, rowWidth));
        }

        return totalSize;
    }

    private static void ValidateParameters(in VideoCodecParameters parameters)
    {
        if (parameters.Width < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters.Width, "Width должен быть больше 0.");
        }

        if (parameters.Height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters.Height, "Height должен быть больше 0.");
        }

        if (parameters.PixelFormat == VideoPixelFormat.Unknown)
        {
            throw new ArgumentException("PixelFormat Unknown не поддерживается.", nameof(parameters));
        }
    }
}