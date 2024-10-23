using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using Atom.Threading;

namespace Atom.Media;

/// <summary>
/// Представляет контекст медиа формата.
/// </summary>
public unsafe class VideoStream : Stream
{
    private readonly FormatContext* input;
    private readonly FormatContext* output;
    private MediaStream* inputVideoStream;
    private MediaStream* outputVideoStream;
    private CodecContext* inputCodecContext;
    private CodecContext* outputCodecContext;
    private MediaFrame* frame;
    private MediaPacket* packet;
    private Task? processingTask;
    private CancellationTokenSource cts;
    private bool isProcessing;
    private bool isReady;

    private Size resolution;
    private MediaFormat format;

    private bool isDisposed;

    private string inputPath;
    private string outputPath;
    private int frameRate;

    /// <summary>
    /// Указывает, был ли закрыт медиа вход.
    /// </summary>
    public bool IsClosed { get; protected set; }

    /// <inheritdoc/>
    public override bool CanRead => !string.IsNullOrEmpty(inputPath) && inputPath is not "/dev/null" && isReady;

    /// <inheritdoc/>
    public override bool CanSeek { get; } = true;

    /// <inheritdoc/>
    public override bool CanWrite => !string.IsNullOrEmpty(outputPath) && outputPath is not "/dev/null" && isReady;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// Получает или задает формат пикселей для потока.
    /// </summary>
    public MediaFormat Format
    {
        get => format;

        set
        {
            format = value;
            SetUpOutput();
        }
    }

    /// <summary>
    /// Получает или задает разрешение видео для потока.
    /// </summary>
    public Size Resolution
    {
        get => resolution;

        set
        {
            resolution = value;
            SetUpOutput();
        }
    }

    /// <summary>
    /// Частота кадров.
    /// </summary>
    public int FrameRate
    {
        get => frameRate;

        set
        {
            frameRate = value;
            SetUpOutput();
        }
    }

    /// <summary>
    /// Входной поток.
    /// </summary>
    public string Input
    {
        get => inputPath;

        set
        {
            inputPath = value;
            SetUpInput();
        }
    }

    /// <summary>
    /// Выходной поток.
    /// </summary>
    public string Output
    {
        get => outputPath;

        set
        {
            outputPath = value;
            SetUpOutput();
        }
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VideoStream"/>.
    /// </summary>
    /// <param name="inputPath">Путь к файлу видео.</param>
    /// <param name="outputPath">Путь к видеоустройству.</param>
    /// <param name="resolution">Разрешение видео.</param>
    /// <param name="pixelFormat">Формат пикселей видео.</param>
    /// <param name="frameRate">Частота кадров.</param>
    public VideoStream(string inputPath, string outputPath, Size resolution, MediaFormat pixelFormat, int frameRate)
    {
        this.inputPath = inputPath;
        this.outputPath = outputPath;
        this.resolution = resolution;
        Format = pixelFormat;
        this.frameRate = frameRate;

        cts = new CancellationTokenSource();

        SetUpInput();
        SetUpOutput();
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VideoStream"/>.
    /// </summary>
    /// <param name="url">URI-адрес видеофайла.</param>
    /// <param name="outputPath">Путь к видеоустройству.</param>
    /// <param name="resolution">Разрешение видео.</param>
    /// <param name="pixelFormat">Формат пикселей видео.</param>
    /// <param name="frameRate">Частота кадров.</param>
    public VideoStream([NotNull] Uri url, string outputPath, Size resolution, MediaFormat pixelFormat, int frameRate)
        : this(url.AbsoluteUri, outputPath, resolution, pixelFormat, frameRate) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VideoStream"/>.
    /// </summary>
    /// <param name="outputPath">Путь к видеоустройству.</param>
    /// <param name="resolution">Разрешение видео.</param>
    /// <param name="pixelFormat">Формат пикселей видео.</param>
    /// <param name="frameRate">Частота кадров.</param>
    public VideoStream(string outputPath, Size resolution, MediaFormat pixelFormat, int frameRate)
        : this(string.Empty, outputPath, resolution, pixelFormat, frameRate) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VideoStream"/>.
    /// </summary>
    /// <param name="resolution">Разрешение видео.</param>
    /// <param name="pixelFormat">Формат пикселей видео.</param>
    /// <param name="frameRate">Частота кадров.</param>
    public VideoStream(Size resolution, MediaFormat pixelFormat, int frameRate) : this(string.Empty, resolution, pixelFormat, frameRate) { }

    /// <summary>
    /// Деструктор класса MediaStream, вызывающий метод Dispose(false).
    /// </summary>
    ~VideoStream() => Dispose(disposing: false);

    static VideoStream()
    {
        _ = FFmpeg.Format.NetworkInit();
        _ = FFmpeg.Device.RegisterAll();
    }

    private void ProcessVideo(CancellationToken cancellationToken)
    {
        Wait.Until(() => !CanWrite);
        isProcessing = true;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!CanRead) GenerateWhiteNoise();

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!CanRead)
                {
                    GenerateWhiteNoiseFrame();
                    continue;
                }

                if (FFmpeg.Format.ReadFrame(input, packet) < 0) break;

                if (packet->stream_index == inputVideoStream->index)
                {
                    if (FFmpeg.Codec.SendPacket(inputCodecContext, packet) < 0) throw new VideoStreamException("Не удалось отправить пакет в кодек");
                    while (FFmpeg.Codec.ReceiveFrame(inputCodecContext, frame) is 0) ProcessFrame();
                }

                if (FFmpeg.Codec.UnRefPacket(packet) < 0) throw new VideoStreamException("Не удалось освободить пакет");
            }

            inputPath = string.Empty;
        }

        isProcessing = false;
    }

    private void SetUpInput()
    {
        isReady = default;

        if (isProcessing && processingTask is not null)
        {
            cts.Cancel();
            processingTask.Wait();
            CloseInput();
        }

        cts = new CancellationTokenSource();
        processingTask = Task.Run(() => ProcessVideo(cts.Token));

        if (!CanRead) return;

        fixed (FormatContext** ctx = &input)
            if (FFmpeg.Format.OpenInput(ctx, inputPath, default, default) < 0)
                throw new VideoStreamException("Не удалось инициализировать контекст входного формата");

        if (FFmpeg.Format.FindStreamInfo(input, default) < 0)
            throw new VideoStreamException("Не удалось найти информацию о входном потоке");

        inputVideoStream = FindVideoStream(input);
        inputCodecContext = FFmpeg.Codec.AllocContext3(FFmpeg.Codec.FindDecoder(inputVideoStream->codecpar->codec_id));

        if (FFmpeg.Codec.ParametersToContext(inputCodecContext, inputVideoStream->codecpar) < 0)
            throw new VideoStreamException("Не удалось передать параметры в контекст входного формата");

        if (FFmpeg.Codec.Open2(inputCodecContext, FFmpeg.Codec.FindDecoder(inputVideoStream->codecpar->codec_id), default) < 0)
            throw new VideoStreamException("Не удалось открыть кодек входного формата");

        frame = FFmpeg.Util.FrameAlloc();
        packet = FFmpeg.Util.PacketAlloc();

        isReady = true;
    }

    private void SetUpOutput()
    {
        isReady = default;

        if (isProcessing && processingTask is not null)
        {
            cts.Cancel();
            processingTask.Wait();
            CloseOutput();
        }

        cts = new CancellationTokenSource();
        processingTask = Task.Run(() => ProcessVideo(cts.Token));

        if (string.IsNullOrEmpty(outputPath) || outputPath is "/dev/null") return;

        fixed (FormatContext** ctx = &output)
            if (FFmpeg.Format.AllocOutputContext2(ctx, default, default, outputPath) < 0)
                throw new VideoStreamException("Не удалось выделить память для контекста выходного формата");

        outputVideoStream = FFmpeg.Format.NewStream(output, default);
        outputCodecContext = FFmpeg.Codec.AllocContext3(FFmpeg.Codec.FindEncoder(format.AsCodecID()));

        outputCodecContext->width = resolution.Width;
        outputCodecContext->height = resolution.Height;
        outputCodecContext->time_base = new Ratio { num = 1, den = frameRate };
        outputCodecContext->pix_fmt = format.AsPixelFormat();

        if (FFmpeg.Codec.Open2(outputCodecContext, FFmpeg.Codec.FindEncoder(outputCodecContext->codec_id), null) < 0)
            throw new VideoStreamException("Не удалось открыть кодек выходного формата");

        if (FFmpeg.Codec.ParametersFromContext(outputVideoStream->codecpar, outputCodecContext) < 0)
            throw new VideoStreamException("Не удалось передать параметры в контекст выходного формата");

        if (FFmpeg.Format.IoOpen(&output->pb, outputPath, 2) < 0)
            throw new VideoStreamException("Не удалось открыть поток записи");

        if (FFmpeg.Format.WriteHeader(output, default) < 0)
            throw new VideoStreamException("Не удалось записать заголовок формата");

        isReady = true;
    }

    private void GenerateWhiteNoise()
    {
        frame = FFmpeg.Util.FrameAlloc();
        frame->format = format.AsPixelFormat();
        frame->width = resolution.Width;
        frame->height = resolution.Height;

        if (FFmpeg.Util.FrameGetBuffer(frame, 32) < 0) throw new VideoStreamException("Не удалось выделить буфер для фрейма записи");
    }

    private void GenerateWhiteNoiseFrame()
    {
        for (var y = 0; y < frame->height; ++y)
        {
            for (var x = 0; x < frame->width; ++x)
            {
                var offset = y * frame->linesize[0] + x;
                frame->data[0][offset] = 255;
            }
        }

        if (FFmpeg.Codec.SendFrame(outputCodecContext, frame) < 0) throw new VideoStreamException("Не удалось отправить фрейм в кодек");

        while (FFmpeg.Codec.ReceivePacket(outputCodecContext, packet) is 0)
        {
            FFmpeg.Codec.PacketRescaleTS(packet, outputCodecContext->time_base, outputVideoStream->time_base);
            if (FFmpeg.Format.InterleavedWriteFrame(output, packet) < 0) throw new VideoStreamException("Не удалось записать фрейм");
        }
    }

    private void ProcessFrame()
    {
        if (FFmpeg.Codec.SendFrame(outputCodecContext, frame) < 0) throw new VideoStreamException("Не удалось отправить фрейм в кодек");

        while (FFmpeg.Codec.ReceivePacket(outputCodecContext, packet) is 0)
        {
            FFmpeg.Codec.PacketRescaleTS(packet, inputCodecContext->time_base, outputVideoStream->time_base);
            if (FFmpeg.Format.InterleavedWriteFrame(output, packet) < 0) throw new VideoStreamException("Не удалось записать фрейм");
        }
    }

    private void CloseInput()
    {
        fixed (MediaFrame** frm = &frame) FFmpeg.Util.FrameFree(frm);
        fixed (MediaPacket** pkt = &packet) FFmpeg.Codec.PacketFree(pkt);
        fixed (CodecContext** ctx = &inputCodecContext) FFmpeg.Codec.FreeContext(ctx);
        fixed (FormatContext** ctx = &input) FFmpeg.Format.CloseInput(ctx);
    }

    private void CloseOutput()
    {
        fixed (CodecContext** ctx = &outputCodecContext) FFmpeg.Codec.FreeContext(ctx);
        FFmpeg.Format.FreeContext(output);
    }

    /// <summary>
    /// Освобождает неуправляемые ресурсы, используемые объектом.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (isDisposed) return;
        isDisposed = true;

        if (processingTask is not null)
        {
            cts.Cancel();
            processingTask.Wait();
        }

        Close();
        cts.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>
    /// Закрывает медиа вход.
    /// </summary>
    public override void Close()
    {
        if (IsClosed) return;
        IsClosed = true;

        CloseInput();
        CloseOutput();
    }

    /// <inheritdoc/>
    public override void Flush() { }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private static MediaStream* FindVideoStream(FormatContext* formatContext)
    {
        for (var i = 0; i < formatContext->nb_streams; ++i)
            if (formatContext->streams[i]->codecpar->codec_type is 0)
                return formatContext->streams[i];

        return null;
    }

}