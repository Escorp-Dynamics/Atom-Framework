using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using Atom.Threading;

namespace Atom.Media;

/// <summary>
/// Представляет контекст медиа формата.
/// </summary>
public class VideoStream : Stream
{
    private const string DevNull = "/dev/null";

    private readonly SemaphoreSlim locker = new(1, 1);

    private readonly unsafe FormatContext* input = null;
    private readonly unsafe FormatContext* output = null;

    private int inputVideoStreamIndex = -1;
    private int inputAudioStreamIndex = -1;

    private unsafe CodecContext* videoDecoder;
    private unsafe CodecContext* audioDecoder;

    private int outputVideoStreamIndex = -1;
    private int outputAudioStreamIndex = -1;

    private unsafe CodecContext* videoEncoder = null;
    private unsafe CodecContext* audioEncoder = null;

    private Task? processingTask;
    private CancellationTokenSource cts;
    private bool isInputReady;
    private bool isOutputReady;
    private bool isDevice;

    private Size resolution;
    private int frameRate;
    private int audioSampleRate;
    private int audioChannels;

    private bool isDisposed;

    private string inputPath;
    private string outputPath;

    private long readyFrames;
    private long neededFrames;

    private long videoPts;
    private long audioPts;

    /// <summary>
    /// Указывает, был ли закрыт медиа вход.
    /// </summary>
    public bool IsClosed { get; protected set; }

    /// <inheritdoc/>
    public override bool CanRead => !string.IsNullOrEmpty(inputPath) && inputPath is not DevNull && isInputReady;

    /// <inheritdoc/>
    public override bool CanSeek { get; } = true;

    /// <inheritdoc/>
    public override bool CanWrite => !string.IsNullOrEmpty(outputPath) && outputPath is not DevNull && isOutputReady;

    /// <inheritdoc/>
    public override long Length => readyFrames;

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
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
            SetUpVideoEncoder();
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
            SetUpDecoder();
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
            SetUpEncoder();
        }
    }

    /// <summary>
    /// Определяет, является ли поток записи зацикленным.
    /// </summary>
    public bool IsLooped { get; set; }

    /// <summary>
    /// Определяет, будет ли использоваться звук, если он доступен.
    /// </summary>
    public bool IsMuted { get; set; }

    /// <summary>
    /// Частота кадров.
    /// </summary>
    public unsafe int FrameRate
    {
        get => frameRate;

        set
        {
            frameRate = value;
            SetUpVideoEncoder();
        }
    }

    /// <summary>
    /// Частота аудиосемплов.
    /// </summary>
    public unsafe int AudioSampleRate
    {
        get => audioSampleRate;

        set
        {
            audioSampleRate = value;
            SetUpAudioEncoder();
        }
    }

    /// <summary>
    /// Количество аудиоканалов.
    /// </summary>
    public unsafe int AudioChannels
    {
        get => audioChannels;

        set
        {
            audioChannels = value;
            SetUpAudioEncoder();
        }
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VideoStream"/>.
    /// </summary>
    /// <param name="inputPath">Путь к файлу видео.</param>
    /// <param name="outputPath">Путь к видеоустройству.</param>
    /// <param name="resolution">Разрешение видео.</param>
    public VideoStream(string inputPath, string outputPath, Size resolution)
    {
        this.inputPath = inputPath;
        this.outputPath = outputPath;
        this.resolution = resolution;

        if (this.resolution == default) this.resolution = new Size(640, 480);
        audioSampleRate = 44100;
        audioChannels = 2;
        frameRate = 25;

        cts = new CancellationTokenSource();

        if (!string.IsNullOrEmpty(inputPath)) SetUpDecoder();
        if (!string.IsNullOrEmpty(outputPath)) SetUpEncoder();
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VideoStream"/>.
    /// </summary>
    /// <param name="url">URI-адрес видеофайла.</param>
    /// <param name="outputPath">Путь к видеоустройству.</param>
    /// <param name="resolution">Разрешение видео.</param>
    public VideoStream([NotNull] Uri url, string outputPath, Size resolution) : this(url.AbsoluteUri, outputPath, resolution) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VideoStream"/>.
    /// </summary>
    /// <param name="outputPath">Путь к видеоустройству.</param>
    /// <param name="resolution">Разрешение видео.</param>
    public VideoStream(string outputPath, Size resolution) : this(string.Empty, outputPath, resolution) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VideoStream"/>.
    /// </summary>
    /// <param name="resolution">Разрешение видео.</param>
    public VideoStream(Size resolution) : this(string.Empty, resolution) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VideoStream"/>.
    /// </summary>
    /// <param name="inputPath">Путь к файлу видео.</param>
    /// <param name="outputPath">Путь к видеоустройству.</param>
    public VideoStream(string inputPath, string outputPath) : this(inputPath, outputPath, default) { }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VideoStream"/>.
    /// </summary>
    /// <param name="url">URI-адрес видеофайла.</param>
    /// <param name="outputPath">Путь к видеоустройству.</param>
    public VideoStream([NotNull] Uri url, string outputPath) : this(url.AbsoluteUri, outputPath, default) { }

    /// <summary>
    /// Деструктор класса MediaStream, вызывающий метод Dispose(false).
    /// </summary>
    ~VideoStream() => Dispose(disposing: false);

    static unsafe VideoStream()
    {
#if DEBUG
        FFmpeg.Util.SetLogLevel(40);
#else
        FFmpeg.Util.SetLogLevel(0);
#endif
        FFmpeg.Util.SetLogFlags(2);
        FFmpeg.Device.RegisterAll();
        FFmpeg.Format.NetworkInit();
    }

    private unsafe void SetUpVideoDecoder()
    {
        locker.Wait();

        fixed (CodecContext** ctx = &videoDecoder)
        {
            if (ctx is not null)
            {
                FFmpeg.Codec.Close(videoDecoder);
                FFmpeg.Codec.FreeContext(ctx);
                videoDecoder = default;
            }
        }

        AVCodec* decoder = null;

        inputVideoStreamIndex = FFmpeg.Format.FindBestStream(input, 0, -1, -1, &decoder, 0);

        if (inputVideoStreamIndex < 0)
        {
            locker.Release();
            return;
        }

        var inputStream = input->streams[inputVideoStreamIndex];

        if (inputStream is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось найти входной видеопоток");
        }

        videoDecoder = FFmpeg.Codec.AllocContext3(decoder);

        if (videoDecoder is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось выделить контекст кодека входного видеопотока");
        }

        FFmpeg.Codec.ParametersToContext(videoDecoder, inputStream->codecpar)
            .ThrowIfErrors("Не удалось передать параметры в контекст входного формата видеопотока", locker);

        FFmpeg.Codec.Open2(videoDecoder, decoder, default)
            .ThrowIfErrors("Не удалось открыть кодек входного формата видеопотока", locker);

        if (FFmpeg.Codec.IsOpen(videoDecoder).ThrowIfErrors("Не удалось проверить открытие декодировщика", locker) is 0)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось открыть декодировщик");
        }

        if (FFmpeg.Codec.IsDecoder(videoDecoder->codec).ThrowIfErrors("Не удалось проверить декодировщик", locker) is 0)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось проверить декодировщик");
        }

        locker.Release();
        SetUpVideoEncoder();
    }

    private unsafe void SetUpAudioDecoder()
    {
        locker.Wait();

        fixed (CodecContext** ctx = &audioDecoder)
        {
            if (ctx is not null)
            {
                FFmpeg.Codec.Close(audioDecoder);
                FFmpeg.Codec.FreeContext(ctx);
                audioDecoder = default;
            }
        }

        AVCodec* decoder = null;

        inputAudioStreamIndex = FFmpeg.Format.FindBestStream(input, 1, -1, -1, &decoder, 0);

        if (inputAudioStreamIndex < 0)
        {
            locker.Release();
            return;
        }

        var inputStream = input->streams[inputAudioStreamIndex];

        if (inputStream is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось найти входной аудиопоток");
        }

        audioDecoder = FFmpeg.Codec.AllocContext3(decoder);

        if (audioDecoder is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось выделить контекст кодека входного аудиопотока");
        }

        FFmpeg.Codec.ParametersToContext(audioDecoder, inputStream->codecpar)
            .ThrowIfErrors("Не удалось передать параметры в контекст входного формата аудиопотока", locker);

        FFmpeg.Codec.Open2(audioDecoder, decoder, default)
            .ThrowIfErrors("Не удалось открыть кодек входного формата аудиопотока", locker);

        if (FFmpeg.Codec.IsOpen(videoDecoder).ThrowIfErrors("Не удалось проверить открытие декодировщика", locker) is 0)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось открыть декодировщик");
        }

        if (FFmpeg.Codec.IsDecoder(videoDecoder->codec).ThrowIfErrors("Не удалось проверить декодировщик", locker) is 0)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось проверить декодировщик");
        }

        locker.Release();
        SetUpAudioEncoder();
    }

    private void SetUpDecoder()
    {
        locker.Wait();

        inputVideoStreamIndex = -1;
        inputAudioStreamIndex = -1;
        isInputReady = default;

        if (processingTask is not null)
        {
            cts.Cancel();
            processingTask = default;
        }

        CloseDecoder();
        cts = new CancellationTokenSource();
        processingTask = Task.Run(() => Process(cts.Token));

        locker.Release();

        if (string.IsNullOrEmpty(inputPath) || inputPath is DevNull) return;

        unsafe
        {
            fixed (FormatContext** ctx = &input)
            {
                FFmpeg.Format.OpenInput(ctx, inputPath, default, default)
                .ThrowIfErrors("Не удалось инициализировать контекст входного формата");
            }

            FFmpeg.Format.FindStreamInfo(input, default).ThrowIfErrors("Не удалось найти информацию о входном потоке");

            SetUpVideoDecoder();
            SetUpAudioDecoder();

            if (videoDecoder is null && audioDecoder is null) throw new VideoStreamException("Не удалось найти ни одного аудио и видео потока");
            if (resolution == default && videoDecoder is not null) resolution = new Size(videoDecoder->width, videoDecoder->height);
        }

        isInputReady = true;
    }

    private unsafe void SetUpVideoEncoder()
    {
        locker.Wait();

        fixed (CodecContext** ctx = &videoEncoder)
        {
            if (ctx is not null)
            {
                FFmpeg.Codec.Close(videoEncoder);
                FFmpeg.Codec.FreeContext(ctx);
                videoEncoder = default;
            }
        }

        if (output is null || output->oformat->VideoCodec is MediaCodec.NONE)
        {
            locker.Release();
            return;
        }

        MediaStream* inputStream = default;

        if (input is not null)
        {
            inputStream = input->streams[inputVideoStreamIndex];

            if (inputStream is null)
            {
                locker.Release();
                throw new VideoStreamException("Не удалось найти входной видеопоток");
            }
        }

        var outputStream = outputVideoStreamIndex < 0
            ? FFmpeg.Format.NewStream(output, default)
            : output->streams[outputVideoStreamIndex];

        if (outputStream is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось открыть поток записи видео");
        }

        var codec = FFmpeg.Codec.FindEncoder(output->oformat->VideoCodec);

        if (codec is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось найти декодер формата для выходного видеопотока");
        }

        videoEncoder = FFmpeg.Codec.AllocContext3(codec);

        if (videoEncoder is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось выделить контекст кодека выходного видеопотока");
        }

        if (inputStream is null)
        {
            videoEncoder->bit_rate = 4000000;
            videoEncoder->rc_buffer_size = 230 * 1024;
            videoEncoder->rc_min_rate = 512 * 1024;
            videoEncoder->rc_max_rate = 1024 * 1024;
            videoEncoder->width = resolution.Width;
            videoEncoder->height = resolution.Height;
            videoEncoder->gop_size = 12;
            videoEncoder->max_b_frames = 2;
        }
        else
        {
            videoEncoder->bit_rate = videoDecoder->bit_rate;
            videoEncoder->rc_buffer_size = videoDecoder->rc_buffer_size;
            videoEncoder->rc_min_rate = videoDecoder->rc_min_rate;
            videoEncoder->rc_max_rate = videoDecoder->rc_max_rate;
            videoEncoder->width = videoDecoder->width;
            videoEncoder->height = videoDecoder->height;
            videoEncoder->gop_size = videoDecoder->gop_size;
            videoEncoder->max_b_frames = videoDecoder->max_b_frames;
            videoEncoder->flags = videoDecoder->flags;
        }

        videoEncoder->PixelFormat = videoEncoder->Kind.ToPixelFormat();

        if (videoEncoder->Kind is MediaCodec.MJPEG)
        {
            videoEncoder->max_b_frames = default;
            videoEncoder->bit_rate = videoEncoder->rc_min_rate = videoEncoder->rc_max_rate;
        }
        else if (videoEncoder->Kind is MediaCodec.WEBP)
        {
            videoEncoder->bit_rate = default;
            videoEncoder->flags |= 1;
            videoEncoder->flags |= 2;
            videoEncoder->flags |= 524288;
        }
        else if (videoEncoder->Kind is MediaCodec.H264)
        {
            videoEncoder->width = (videoEncoder->width % 2 is 0) ? videoEncoder->width : videoEncoder->width + 1;
            videoEncoder->height = (videoEncoder->height % 2 is 0) ? videoEncoder->height : videoEncoder->height + 1;
        }

        videoEncoder->time_base = new Ratio { num = 1, den = frameRate };
        videoEncoder->framerate = new Ratio { num = frameRate, den = 1 };
        videoEncoder->delay = default;
        outputStream->time_base = videoEncoder->time_base;

        if ((output->oformat->flags & 64) is not 0)
        {
            videoEncoder->flags |= 4194304;
            output->flags |= 4194304;
        }

        FFmpeg.Codec.Open2(videoEncoder, codec, default)
            .ThrowIfErrors("Не удалось открыть кодек выходного формата видеопотока", locker);

        if (FFmpeg.Codec.IsOpen(videoEncoder).ThrowIfErrors("Не удалось проверить открытие кодировщика", locker) is 0)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось открыть кодировщик");
        }

        if (FFmpeg.Codec.IsEncoder(videoEncoder->codec).ThrowIfErrors("Не удалось проверить кодировщик", locker) is 0)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось проверить кодировщик");
        }

        FFmpeg.Codec.ParametersFromContext(outputStream->codecpar, videoEncoder)
            .ThrowIfErrors("Не удалось передать параметры в контекст выходного формата видеопотока", locker);

        outputVideoStreamIndex = outputStream->index;
        locker.Release();
    }

    private unsafe void SetUpAudioEncoder()
    {
        locker.Wait();

        fixed (CodecContext** ctx = &audioEncoder)
        {
            if (ctx is not null)
            {
                FFmpeg.Codec.Close(audioEncoder);
                FFmpeg.Codec.FreeContext(ctx);
                audioEncoder = default;
            }
        }

        if (output is null || output->oformat->AudioCodec is MediaCodec.NONE)
        {
            locker.Release();
            return;
        }

        MediaStream* inputStream = default;

        if (input is not null && inputAudioStreamIndex >= 0)
        {
            inputStream = input->streams[inputAudioStreamIndex];

            if (inputStream is null)
            {
                locker.Release();
                throw new VideoStreamException("Не удалось найти входной аудиопоток");
            }
        }

        var outputStream = outputAudioStreamIndex < 0
            ? FFmpeg.Format.NewStream(output, default)
            : output->streams[inputAudioStreamIndex];

        if (outputStream is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось открыть поток записи аудио");
        }

        var codec = FFmpeg.Codec.FindEncoder(output->oformat->AudioCodec);

        if (codec is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось найти декодер формата для выходного аудиопотока");
        }

        audioEncoder = FFmpeg.Codec.AllocContext3(codec);

        if (audioEncoder is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось выделить контекст кодека выходного аудиопотока");
        }

        if (inputStream is null)
        {
            ChannelLayout cl = default;
            FFmpeg.Util.DefaultChannelLayout(&cl, audioChannels);

            audioEncoder->sample_rate = audioEncoder->Kind is MediaCodec.OPUS ? 48000 : audioSampleRate;
            audioEncoder->ChannelLayout = cl;
            audioEncoder->SampleFormat = output->oformat->AudioCodec.ToSampleFormat();
            audioEncoder->time_base = new Ratio { num = 1, den = audioEncoder->sample_rate };
            audioEncoder->framerate = new Ratio { num = audioEncoder->sample_rate, den = 1 };
            audioEncoder->bit_rate = 256000;
            audioEncoder->rc_max_rate = 2500000;
            audioEncoder->trellis = 1;
            audioEncoder->qmax = 51;
            audioEncoder->gop_size = 12;
        }
        else
        {
            audioEncoder->sample_rate = audioDecoder->sample_rate;
            audioEncoder->ChannelLayout = audioDecoder->ChannelLayout;
            audioEncoder->SampleFormat = audioDecoder->SampleFormat;
            audioEncoder->time_base = audioDecoder->time_base;
            audioEncoder->bit_rate = audioDecoder->bit_rate;
            audioEncoder->rc_max_rate = audioDecoder->rc_max_rate;
            audioEncoder->trellis = audioDecoder->trellis;
            audioEncoder->qmax = audioDecoder->qmax;
            audioEncoder->gop_size = audioDecoder->gop_size;
        }

        if (inputStream is not null) outputStream->time_base = inputStream->time_base;

        FFmpeg.Codec.Open2(audioEncoder, codec, default)
                .ThrowIfErrors("Не удалось открыть кодек выходного формата аудиопотока", locker);

        if (FFmpeg.Codec.IsOpen(audioEncoder).ThrowIfErrors("Не удалось проверить открытие кодировщика", locker) is 0)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось открыть кодировщик");
        }

        if (FFmpeg.Codec.IsEncoder(audioEncoder->codec).ThrowIfErrors("Не удалось проверить кодировщик", locker) is 0)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось проверить кодировщик");
        }

        FFmpeg.Codec.ParametersFromContext(outputStream->codecpar, audioEncoder)
            .ThrowIfErrors("Не удалось передать параметры в контекст выходного формата аудиопотока", locker);

        outputAudioStreamIndex = outputStream->index;
        locker.Release();
    }

    private void SetUpEncoder()
    {
        locker.Wait();

        outputVideoStreamIndex = -1;
        outputAudioStreamIndex = -1;
        videoPts = default;
        audioPts = default;
        readyFrames = default;

        if (processingTask is not null)
        {
            cts.Cancel();
            processingTask = default;
        }

        CloseEncoder();

        isOutputReady = default;
        cts = new CancellationTokenSource();
        processingTask = Task.Run(() => Process(cts.Token));

        locker.Release();

        if (string.IsNullOrEmpty(outputPath) || outputPath is DevNull) return;

        unsafe
        {
            isDevice = outputPath.StartsWith("/dev/");
            var shortName = isDevice ? "v4l2" : default;
            var fileName = isDevice ? default : outputPath;

            var outputFormat = FFmpeg.Format.GuessFormat(shortName, fileName, default);
            if (outputFormat is null) throw new VideoStreamException("Не удалось угадать формат вывода");

            fixed (FormatContext** ctx = &output)
            {
                FFmpeg.Format.AllocOutputContext2(ctx, outputFormat, default, outputPath)
                .ThrowIfErrors("Не удалось выделить память для контекста выходного формата");
            }

            SetUpVideoEncoder();
            SetUpAudioEncoder();

            FFmpeg.Format.IoOpen(&output->pb, outputPath, 2).ThrowIfErrors("Не удалось открыть поток записи");

            output->max_index_size = 1 << 20;
            output->max_picture_buffer = 1 << 20;

            FFmpeg.Format.WriteHeader(output, default).ThrowIfErrors("Не удалось записать заголовок формата");
        }

        isOutputReady = true;
    }

    private unsafe void Encode(CodecContext* encoder, MediaStream* inputStream, MediaStream* outputStream, MediaFrame* frame, int samples, Ratio inputTimeBase, Ratio outputTimeBase)
    {
        frame->pts = inputStream->index == inputVideoStreamIndex ? videoPts : audioPts;
        frame->pict_type = (videoPts is 0) ? 1 : 2;
        frame->flags = 0;
        frame->duration = 1;
        frame->time_base = outputTimeBase;

        const string packetError = "Не удалось освободить пакет";

        var packet = FFmpeg.Codec.PacketAlloc();
        FFmpeg.Codec.SendFrame(encoder, frame).ThrowIfErrors("Не удалось отправить фрейм в энкодер", locker);
        var result = FFmpeg.Codec.ReceivePacket(encoder, packet);

        if (isDevice)
        {
            packet->dts = packet->pts = packet->stream_index == inputVideoStreamIndex ? videoPts : audioPts;

            if (packet->stream_index == inputVideoStreamIndex)
                ++videoPts;
            else
                audioPts += samples;
        }
        else
        {
            if (packet->stream_index == inputVideoStreamIndex)
                videoPts += FFmpeg.Util.ReScaleQ(1, inputTimeBase, outputTimeBase);
            else
                audioPts += FFmpeg.Util.ReScaleQ(samples, inputTimeBase, outputTimeBase);
        }

        if (result is -11 or -541_478_725)
        {
            FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors(packetError);
            FFmpeg.Codec.PacketFree(&packet);
            Thread.Sleep(10);
            return;
        }

        result.ThrowIfErrors("Не удалось получить пакет из энкодера", locker);

        packet->stream_index = outputStream->index;

        FFmpeg.Format.InterleavedWriteFrame(output, packet).ThrowIfErrors("Не удалось записать фрейм", locker);
        if (packet->stream_index == inputVideoStreamIndex) ++readyFrames;

        FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors(packetError, locker);
        FFmpeg.Codec.PacketFree(&packet);

        if (isDevice) Thread.Sleep(1000 / frameRate);
    }

    private unsafe MediaFrame* Decode(CodecContext* decoder, MediaPacket* packet, int encoderFormat, int encoderWidth, int encoderHeight, Ratio inputTimeBase, Ratio outputTimeBase)
    {
        var frame = FFmpeg.Util.FrameAlloc();

        if (frame is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось выделить память для фрейма");
        }

        var result = FFmpeg.Codec.SendPacket(decoder, packet);

        if (result is -11 or -541_478_725)
        {
            FFmpeg.Util.FrameFree(&frame);
            Thread.Sleep(10);
            return default;
        }

        result.ThrowIfErrors("Не удалось отправить пакет на декодер", locker);
        result = FFmpeg.Codec.ReceiveFrame(decoder, frame);

        if (result is -11 or -541_478_725)
        {
            FFmpeg.Util.FrameFree(&frame);
            Thread.Sleep(10);
            return default;
        }

        result.ThrowIfErrors("Не удалось получить фрейм из декодера", locker);

        var pts = packet->stream_index == inputVideoStreamIndex ? videoPts : audioPts;

        if (packet->stream_index == inputVideoStreamIndex && frame->Format != encoderFormat)
        {
            var convertedFrame = FFmpeg.Util.FrameAlloc();

            if (convertedFrame == null)
            {
                locker.Release();
                throw new VideoStreamException("Не удалось выделить память для преобразованного фрейма");
            }

            convertedFrame->Format = encoderFormat;
            convertedFrame->width = frame->width;
            convertedFrame->height = frame->height;

            FFmpeg.Util.FrameGetBuffer(convertedFrame, 32).ThrowIfErrors("Ошибка выделения буфера для преобразованного фрейма", locker);
            FFmpeg.Util.FrameMakeWritable(convertedFrame).ThrowIfErrors("Не удалось сделать фрейм записываемым", locker);

            var swsContext = FFmpeg.SwScale.GetContext(
                frame->width, frame->height, (PixelFormat)frame->Format,
                encoderWidth, encoderHeight, (PixelFormat)encoderFormat,
                2, default, default, default
            );

            if (swsContext is null)
            {
                locker.Release();
                throw new VideoStreamException("Не удалось создать контекст масштабирования фрейма");
            }

            FFmpeg.SwScale.Scale(
                swsContext,
                frame->data.Source,
                frame->linesize,
                0,
                frame->height,
                convertedFrame->data.Source,
                convertedFrame->linesize
            ).ThrowIfErrors("Не удалось отмасштабировать фрейм", locker);

            FFmpeg.SwScale.FreeContext(swsContext);

            convertedFrame->pts = FFmpeg.Util.ReScaleQ(pts, inputTimeBase, outputTimeBase);
            convertedFrame->pict_type = (videoPts is 0) ? 1 : 2;
            convertedFrame->flags = 0;
            convertedFrame->time_base = outputTimeBase;

            FFmpeg.Util.FrameFree(&frame);
            return convertedFrame;
        }

        return frame;
    }

    private unsafe bool ProcessInput()
    {
        if (!CanRead || !CanWrite) return default;

        var packet = FFmpeg.Codec.PacketAlloc();

        if (packet is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось выделить память для пакета");
        }

        const string packetError = "Не удалось освободить пакет";

        if (FFmpeg.Format.ReadFrame(input, packet) < 0)
        {
            if (IsLooped || readyFrames < neededFrames) FFmpeg.Format.SeekFrame(input, -1, 0, 1).ThrowIfErrors("Ошибка перемещения указателя на начало", locker);

            FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors(packetError, locker);
            FFmpeg.Codec.PacketFree(&packet);

            return IsLooped || readyFrames < neededFrames;
        }

        if (packet->stream_index == inputAudioStreamIndex && IsMuted)
        {
            FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors(packetError, locker);
            FFmpeg.Codec.PacketFree(&packet);

            return true;
        }

        var inputStreamIndex = packet->stream_index == inputAudioStreamIndex ? inputAudioStreamIndex : inputVideoStreamIndex;
        var outputStreamIndex = packet->stream_index == inputAudioStreamIndex ? outputAudioStreamIndex : outputVideoStreamIndex;

        var inputStream = input->streams[inputStreamIndex];
        var outputStream = output->streams[outputStreamIndex];

        var decoder = packet->stream_index == inputVideoStreamIndex ? videoDecoder : audioDecoder;
        var encoder = packet->stream_index == inputVideoStreamIndex ? videoEncoder : audioEncoder;

        var frame = Decode(decoder, packet, (int)encoder->PixelFormat, encoder->width, encoder->height, inputStream->time_base, outputStream->time_base);

        if (frame is not null)
        {
            if (inputStream->nb_frames < 2 && inputStream->duration <= 1)
            {
                while (CanWrite && (IsLooped || readyFrames < neededFrames))
                {
                    Encode(encoder, inputStream, outputStream, frame, frame->nb_samples, inputStream->time_base, outputStream->time_base);
                    if (output->oformat->IsImage && readyFrames > 0) break;
                }

                FFmpeg.Util.FrameFree(&frame);
                return default;
            }

            Encode(encoder, inputStream, outputStream, frame, frame->nb_samples, inputStream->time_base, outputStream->time_base);
            FFmpeg.Util.FrameFree(&frame);
        }

        FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors(packetError, locker);
        FFmpeg.Codec.PacketFree(&packet);

        return !output->oformat->IsImage || readyFrames <= 0;
    }

    private unsafe bool ProcessWhiteNoise()
    {
        if ((CanRead && !isDevice) || !CanWrite || (neededFrames > 0 && readyFrames >= neededFrames) || (readyFrames > 0 && output->oformat->IsImage)) return default;
        if (videoEncoder is not null) WhiteNoiseGenerator.WriteVideoFrame(videoEncoder, output, outputVideoStreamIndex, ref videoPts, locker);
        if (!output->oformat->IsImage && !IsMuted && audioEncoder is not null) WhiteNoiseGenerator.WriteAudioFrame(audioEncoder, output, outputAudioStreamIndex, ref audioPts, locker);

        ++readyFrames;
        if (neededFrames <= 0 || isDevice) Thread.Sleep(1000 / frameRate);

        return neededFrames <= 0 || readyFrames <= neededFrames;
    }

    private unsafe void Process(CancellationToken cancellationToken)
    {
        Wait.Until(() => !CanWrite && !cancellationToken.IsCancellationRequested && !isDisposed);

        while (CanWrite && !cancellationToken.IsCancellationRequested && !isDisposed)
        {
            locker.Wait(CancellationToken.None);

            if (cancellationToken.IsCancellationRequested)
            {
                locker.Release();
                break;
            }

            if (!CanRead)
            {
                if (!ProcessWhiteNoise())
                {
                    locker.Release();
                    break;
                }

                locker.Release();
                continue;
            }

            if (!ProcessInput())
            {
                locker.Release();
                break;
            }

            locker.Release();
        }

        if (!cancellationToken.IsCancellationRequested) cts.Cancel();
    }

    private unsafe void CloseDecoder()
    {
        fixed (CodecContext** ctx = &videoDecoder) if (ctx is not null) FFmpeg.Codec.FreeContext(ctx);
        fixed (CodecContext** ctx = &audioDecoder) if (ctx is not null) FFmpeg.Codec.FreeContext(ctx);
        fixed (FormatContext** ctx = &input) if (ctx is not null) FFmpeg.Format.CloseInput(ctx);
    }

    private unsafe void CloseEncoder()
    {
        if (isOutputReady) FFmpeg.Format.WriteTrailer(output).ThrowIfErrors("Не удалось записать трейлер");
        if (output is not null && output->pb is not null) FFmpeg.Format.IoCloseP(&output->pb).ThrowIfErrors("Не удалось закрыть поток вывода");
        fixed (CodecContext** ctx = &videoEncoder) if (ctx is not null) FFmpeg.Codec.FreeContext(ctx);
        fixed (CodecContext** ctx = &audioEncoder) if (ctx is not null) FFmpeg.Codec.FreeContext(ctx);
        if (output is not null) FFmpeg.Format.FreeContext(output);
    }

    /// <summary>
    /// Освобождает неуправляемые ресурсы, используемые объектом.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (isDisposed) return;
        isDisposed = true;

        if (processingTask is not null) cts.Cancel();

        Close();
        cts.Dispose();
        base.Dispose(disposing);
        locker.Dispose();
    }

    /// <summary>
    /// Закрывает медиа вход.
    /// </summary>
    public override void Close()
    {
        if (IsClosed) return;
        IsClosed = true;

        CloseEncoder();
        CloseDecoder();
    }

    /// <inheritdoc/>
    public override void Flush() => throw new NotSupportedException();

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>
    /// Ожидает завершение записи в выходной поток.
    /// </summary>
    /// <param name="timeout">Таймаут ожидания.</param>
    public unsafe void WaitForEnding(TimeSpan timeout)
    {
        readyFrames = default;
        var timer = Stopwatch.StartNew();
        neededFrames = (long)(frameRate * timeout.TotalSeconds);

        Wait.Until(() => !cts.IsCancellationRequested, TimeSpan.FromMilliseconds(10), timeout);

        Input = string.Empty;

        if (output->oformat->IsImage) return;

        var time = (int)(timeout - timer.Elapsed).TotalMilliseconds;
        if (time > 0) Thread.Sleep(time);
    }

    /// <summary>
    /// Ожидает завершение записи в выходной поток.
    /// </summary>
    public void WaitForEnding()
    {
        Wait.Until(() => !cts.IsCancellationRequested);
        Input = string.Empty;
    }

    /// <summary>
    /// Ожидает завершение записи в выходной поток.
    /// </summary>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public async ValueTask WaitForEndingAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        readyFrames = default;
        var timer = Stopwatch.StartNew();
        neededFrames = (long)(frameRate * timeout.TotalSeconds);

        await Wait.UntilAsync(() => !cts.IsCancellationRequested, TimeSpan.FromMilliseconds(10), timeout, cancellationToken).ConfigureAwait(false);

        Input = string.Empty;

        unsafe
        {
            if (output->oformat->IsImage) return;
        }

        var time = (int)(timeout - timer.Elapsed).TotalMilliseconds;
        if (time > 0) await Task.Delay(time, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ожидает завершение записи в выходной поток.
    /// </summary>
    /// <param name="timeout">Таймаут ожидания.</param>
    public ValueTask WaitForEndingAsync(TimeSpan timeout) => WaitForEndingAsync(timeout, CancellationToken.None);

    /// <summary>
    /// Ожидает завершение записи в выходной поток.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public async ValueTask WaitForEndingAsync(CancellationToken cancellationToken)
    {
        await Wait.UntilAsync(() => !cts.IsCancellationRequested, cancellationToken).ConfigureAwait(false);
        Input = string.Empty;
    }

    /// <summary>
    /// Ожидает завершение записи в выходной поток.
    /// </summary>
    public ValueTask WaitForEndingAsync() => WaitForEndingAsync(CancellationToken.None);
}