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

    private readonly WhiteNoiseGenerator noiseGenerator = new();

    private unsafe readonly FormatContext* input = null;
    private unsafe readonly FormatContext* output = null;

    private int inputVideoStreamIndex = -1;
    private int inputAudioStreamIndex = -1;

    private unsafe CodecContext* inputVideoCodecContext;
    private unsafe CodecContext* inputAudioCodecContext;

    private int outputVideoStreamIndex = -1;
    private int outputAudioStreamIndex = -1;

    private unsafe CodecContext* outputVideoCodecContext = null;
    private unsafe CodecContext* outputAudioCodecContext = null;

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
    public override long Length => throw new NotSupportedException();

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
            if (output is not null) outputVideoCodecContext->framerate = new Ratio { num = 1, den = frameRate };
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
            if (output is not null) outputAudioCodecContext->sample_rate = audioSampleRate;
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

            if (output is not null)
                outputAudioCodecContext->ChannelLayout = new ChannelLayout
                {
                    nb_channels = audioChannels,
                    opaque = outputAudioCodecContext->ChannelLayout.opaque,
                    order = outputAudioCodecContext->ChannelLayout.order,
                    u = outputAudioCodecContext->ChannelLayout.u,
                };
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

        SetUpInput();
        SetUpOutput();
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

    unsafe static VideoStream()
    {
        FFmpeg.Util.SetLogLevel(64);
        FFmpeg.Util.SetLogFlags(2);
        FFmpeg.Device.RegisterAll();
        FFmpeg.Format.NetworkInit();
    }

    private unsafe bool ProcessInput()
    {
        var packet = FFmpeg.Codec.PacketAlloc();
        if (packet is null) throw new VideoStreamException("Не удалось выделить память для пакета");

        const string packetError = "Не удалось освободить пакет";

        if (FFmpeg.Format.ReadFrame(input, packet) < 0)
        {
            if (IsLooped) FFmpeg.Format.SeekFrame(input, -1, 0, 1).ThrowIfErrors("Ошибка перемещения указателя на начало");

            FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors(packetError);
            FFmpeg.Codec.PacketFree(&packet);
            return IsLooped;
        }

        if (packet->stream_index == inputAudioStreamIndex && IsMuted)
        {
            FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors(packetError);
            FFmpeg.Codec.PacketFree(&packet);
            return true;
        }

        var frame = FFmpeg.Util.FrameAlloc();
        if (frame is null) throw new VideoStreamException("Не удалось выделить память для фрейма");

        FFmpeg.Util.FrameMakeWritable(frame);

        var inputStreamIndex = packet->stream_index == inputAudioStreamIndex ? inputAudioStreamIndex : inputVideoStreamIndex;
        var outputStreamIndex = packet->stream_index == inputAudioStreamIndex ? outputAudioStreamIndex : outputVideoStreamIndex;

        var inputStream = input->streams[inputStreamIndex];
        var outputStream = output->streams[outputStreamIndex];

        var decoder = packet->stream_index == inputVideoStreamIndex ? inputVideoCodecContext : inputAudioCodecContext;
        var encoder = packet->stream_index == inputVideoStreamIndex ? outputVideoCodecContext : outputAudioCodecContext;

        var result = FFmpeg.Codec.SendPacket(decoder, packet);

        if (result is -11 or -541_478_725)
        {
            FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors(packetError);
            FFmpeg.Codec.PacketFree(&packet);
            FFmpeg.Util.FrameFree(&frame);
            Thread.Sleep(10);
            return true;
        }

        result.ThrowIfErrors("Не удалось отправить пакет на декодер");
        result = FFmpeg.Codec.ReceiveFrame(decoder, frame);

        if (result is -11 or -541_478_725)
        {
            FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors(packetError);
            FFmpeg.Codec.PacketFree(&packet);
            FFmpeg.Util.FrameFree(&frame);
            Thread.Sleep(10);
            return true;
        }

        result.ThrowIfErrors("Не удалось получить фрейм из декодера");

        var pts = packet->stream_index == inputVideoStreamIndex ? videoPts : audioPts;

        frame->pts = FFmpeg.Util.ReScaleQ(pts, inputStream->time_base, outputStream->time_base);
        frame->pict_type = (videoPts is 0) ? 1 : 2;
        frame->flags = 0;
        frame->time_base = outputStream->time_base;

        if (packet->stream_index == inputVideoStreamIndex)
            videoPts += FFmpeg.Util.ReScaleQ(1, inputStream->time_base, outputStream->time_base);
        else
            audioPts += FFmpeg.Util.ReScaleQ(frame->nb_samples, inputStream->time_base, outputStream->time_base);

        var outputPacket = FFmpeg.Codec.PacketAlloc();
        result = FFmpeg.Codec.SendFrame(encoder, frame);

        if (result is not 0)
        {
            FFmpeg.Util.FrameFree(&frame);
            return true;
        }

        result = FFmpeg.Codec.ReceivePacket(encoder, outputPacket);

        if (result is -11 or -541_478_725)
        {
            FFmpeg.Codec.UnRefPacket(outputPacket).ThrowIfErrors(packetError);
            FFmpeg.Codec.PacketFree(&outputPacket);
            FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors(packetError);
            FFmpeg.Codec.PacketFree(&packet);
            FFmpeg.Util.FrameFree(&frame);
            Thread.Sleep(10);
            return true;
        }

        result.ThrowIfErrors("Не удалось получить пакет из энкодера");

        outputPacket->stream_index = outputStreamIndex;
        //outputPacket->pts = FFmpeg.Util.ReScaleQRnd(frame->pts, inputStream->time_base, outputStream->time_base, 5 | 8192);
        //outputPacket->dts = outputPacket->pts;
        //outputPacket->duration = FFmpeg.Util.ReScaleQ(packet->duration, inputStream->time_base, outputStream->time_base);
        //outputPacket->pos = -1;

        FFmpeg.Format.InterleavedWriteFrame(output, outputPacket).ThrowIfErrors("Не удалось записать фрейм");

        FFmpeg.Codec.UnRefPacket(outputPacket).ThrowIfErrors(packetError);

        FFmpeg.Codec.PacketFree(&outputPacket);
        FFmpeg.Util.FrameFree(&frame);

        FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors("Не удалось освободить пакет");
        FFmpeg.Codec.PacketFree(&packet);

        ++readyFrames;

        if (isDevice) Thread.Sleep(1000 / frameRate);
        return true;
    }

    private unsafe bool ProcessWhiteNoise()
    {
        if (outputVideoCodecContext is not null) noiseGenerator.WriteVideoFrame(outputVideoCodecContext, output, outputVideoStreamIndex);
        if (!IsMuted && outputAudioCodecContext is not null) noiseGenerator.WriteAudioFrame(outputAudioCodecContext, output, outputAudioStreamIndex);

        ++readyFrames;

        if (neededFrames <= 0 || isDevice) Thread.Sleep(1000 / frameRate);
        return neededFrames <= 0 || readyFrames <= neededFrames;
    }

    private unsafe void Process(CancellationToken cancellationToken)
    {
        Wait.Until(() => !CanWrite && !cancellationToken.IsCancellationRequested && !isDisposed);

        if (cancellationToken.IsCancellationRequested || isDisposed) return;

        while (!cancellationToken.IsCancellationRequested && !isDisposed)
        {
            if (!CanRead)
            {
                if (!ProcessWhiteNoise()) break;
                continue;
            }

            if (!ProcessInput()) break;
        }

        if (cancellationToken.IsCancellationRequested) return;

        cts.Cancel();
        processingTask?.Wait(CancellationToken.None);
        processingTask = default;
    }

    private unsafe void SetUpVideoInput()
    {
        AVCodec* decoder = null;

        inputVideoStreamIndex = FFmpeg.Format.FindBestStream(input, 0, -1, -1, &decoder, 0);
        if (inputVideoStreamIndex < 0) return;

        var inputVideoStream = input->streams[inputVideoStreamIndex];
        if (inputVideoStream is null) throw new VideoStreamException("Не удалось найти входной видеопоток");

        inputVideoCodecContext = FFmpeg.Codec.AllocContext3(decoder);
        if (inputVideoCodecContext is null) throw new VideoStreamException("Не удалось выделить контекст кодека входного видеопотока");

        FFmpeg.Codec.ParametersToContext(inputVideoCodecContext, inputVideoStream->codecpar)
            .ThrowIfErrors("Не удалось передать параметры в контекст входного формата видеопотока");

        FFmpeg.Codec.Open2(inputVideoCodecContext, decoder, default)
            .ThrowIfErrors("Не удалось открыть кодек входного формата видеопотока");

        if (FFmpeg.Codec.IsOpen(inputVideoCodecContext).ThrowIfErrors("Не удалось проверить открытие кодировщика") is 0)
            throw new VideoStreamException("Не удалось открыть кодировщик");

        if (FFmpeg.Codec.IsDecoder(inputVideoCodecContext->codec).ThrowIfErrors("Не удалось проверить кодировщик") is 0)
            throw new VideoStreamException("Не удалось проверить кодировщик");
    }

    private unsafe void SetUpAudioInput()
    {
        AVCodec* decoder = null;

        inputAudioStreamIndex = FFmpeg.Format.FindBestStream(input, 1, -1, -1, &decoder, 0);
        if (inputAudioStreamIndex < 0) return;

        var inputAudioStream = input->streams[inputAudioStreamIndex];
        if (inputAudioStream is null) throw new VideoStreamException("Не удалось найти входной аудиопоток");

        inputAudioCodecContext = FFmpeg.Codec.AllocContext3(decoder);
        if (inputAudioCodecContext is null) throw new VideoStreamException("Не удалось выделить контекст кодека входного аудиопотока");

        FFmpeg.Codec.ParametersToContext(inputAudioCodecContext, inputAudioStream->codecpar)
                    .ThrowIfErrors("Не удалось передать параметры в контекст входного формата аудиопотока");

        FFmpeg.Codec.Open2(inputAudioCodecContext, decoder, default)
            .ThrowIfErrors("Не удалось открыть кодек входного формата аудиопотока");
    }

    private void SetUpInput()
    {
        videoPts = default;
        audioPts = default;
        isInputReady = default;

        if (processingTask is not null)
        {
            cts.Cancel();
            processingTask.Wait();
            CloseInput();
        }

        cts = new CancellationTokenSource();
        processingTask = Task.Run(() => Process(cts.Token));

        if (string.IsNullOrEmpty(inputPath) || inputPath is DevNull) return;

        unsafe
        {
            fixed (FormatContext** ctx = &input) FFmpeg.Format.OpenInput(ctx, inputPath, default, default)
                .ThrowIfErrors("Не удалось инициализировать контекст входного формата");

            FFmpeg.Format.FindStreamInfo(input, default).ThrowIfErrors("Не удалось найти информацию о входном потоке");

            SetUpVideoInput();
            SetUpAudioInput();

            if (inputVideoCodecContext is null && inputAudioCodecContext is null) throw new VideoStreamException("Не удалось найти ни одного аудио и видео потока");
            if (resolution == default && inputVideoCodecContext is not null) resolution = new Size(inputVideoCodecContext->width, inputVideoCodecContext->height);
        }

        isInputReady = true;
    }

    private unsafe void SetUpVideoOutput()
    {
        if (output->oformat->VideoCodec is MediaCodec.NONE) return;

        MediaStream* inputVideoStream = default;

        if (input is not null)
        {
            inputVideoStream = input->streams[inputVideoStreamIndex];
            if (inputVideoStream is null) throw new VideoStreamException("Не удалось найти входной видеопоток");
        }

        var outputVideoStream = FFmpeg.Format.NewStream(output, default);
        if (outputVideoStream is null) throw new VideoStreamException("Не удалось открыть поток записи видео");

        var codec = FFmpeg.Codec.FindEncoder(output->oformat->VideoCodec);
        if (codec is null) throw new VideoStreamException("Не удалось найти декодер формата для выходного видеопотока");

        outputVideoCodecContext = FFmpeg.Codec.AllocContext3(codec);
        if (outputVideoCodecContext is null) throw new VideoStreamException("Не удалось выделить контекст кодека выходного видеопотока");

        if (inputVideoStream is null)
        {
            outputVideoCodecContext->bit_rate = 4000000;
            outputVideoCodecContext->rc_buffer_size = 230 * 1024;
            outputVideoCodecContext->rc_min_rate = 512 * 1024;
            outputVideoCodecContext->rc_max_rate = 1024 * 1024;
            outputVideoCodecContext->width = resolution.Width;
            outputVideoCodecContext->height = resolution.Height;
            outputVideoCodecContext->Format = PixelFormat.YUV420P;
            outputVideoCodecContext->time_base = new Ratio { num = 1, den = frameRate };
            outputVideoCodecContext->framerate = new Ratio { num = frameRate, den = 1 };
            outputVideoCodecContext->gop_size = 12;
            outputVideoCodecContext->max_b_frames = 2;

            if (output->oformat->VideoCodec is MediaCodec.MPEG4)
            {
                outputVideoCodecContext->flags |= 262144; // AV_CODEC_FLAG_INTERLACED_DCT
                outputVideoCodecContext->flags |= 536870912; // AV_CODEC_FLAG_INTERLACED_ME
            }
        }
        else
        {
            outputVideoCodecContext->bit_rate = inputVideoCodecContext->bit_rate;
            outputVideoCodecContext->rc_buffer_size = inputVideoCodecContext->rc_buffer_size;
            outputVideoCodecContext->rc_min_rate = inputVideoCodecContext->rc_min_rate;
            outputVideoCodecContext->rc_max_rate = inputVideoCodecContext->rc_max_rate;
            outputVideoCodecContext->width = inputVideoCodecContext->width;
            outputVideoCodecContext->height = inputVideoCodecContext->height;
            outputVideoCodecContext->Format = inputVideoCodecContext->Format;
            outputVideoCodecContext->time_base = inputVideoCodecContext->time_base;
            outputVideoCodecContext->framerate = inputVideoCodecContext->framerate;
            outputVideoCodecContext->gop_size = inputVideoCodecContext->gop_size;
            outputVideoCodecContext->max_b_frames = inputVideoCodecContext->max_b_frames;
            outputVideoCodecContext->flags = inputVideoCodecContext->flags;
        }

        if (outputVideoCodecContext->time_base.num is 0) outputVideoCodecContext->time_base = new Ratio { num = 1, den = frameRate };
        if (inputVideoStream is not null) outputVideoStream->time_base = inputVideoStream->time_base;

        if ((output->oformat->flags & 64) is not 0)
        {
            outputVideoCodecContext->flags |= 4194304;
            output->flags |= 4194304;
        }

        FFmpeg.Codec.Open2(outputVideoCodecContext, codec, default)
            .ThrowIfErrors("Не удалось открыть кодек выходного формата видеопотока");

        if (FFmpeg.Codec.IsOpen(outputVideoCodecContext).ThrowIfErrors("Не удалось проверить открытие декодировщика") is 0)
            throw new VideoStreamException("Не удалось открыть кодировщик");

        if (FFmpeg.Codec.IsEncoder(outputVideoCodecContext->codec).ThrowIfErrors("Не удалось проверить декодировщик") is 0)
            throw new VideoStreamException("Не удалось проверить кодировщик");

        if (inputVideoStream is not null && !isDevice && inputVideoCodecContext->Kind == outputVideoCodecContext->Kind)
            FFmpeg.Codec.ParametersCopy(outputVideoStream->codecpar, inputVideoStream->codecpar)
                .ThrowIfErrors("Не удалось скопировать параметры выходного видеопотока");
        else
            FFmpeg.Codec.ParametersFromContext(outputVideoStream->codecpar, outputVideoCodecContext)
                .ThrowIfErrors("Не удалось передать параметры в контекст выходного формата видеопотока");

        outputVideoStreamIndex = outputVideoStream->index;
    }

    private unsafe void SetUpAudioOutput()
    {
        if (output->oformat->AudioCodec is MediaCodec.NONE) return;

        MediaStream* inputAudioStream = default;

        if (input is not null && inputAudioStreamIndex >= 0)
        {
            inputAudioStream = input->streams[inputAudioStreamIndex];
            if (inputAudioStream is null) throw new VideoStreamException("Не удалось найти выходной аудиопоток");
        }

        var outputAudioStream = FFmpeg.Format.NewStream(output, default);
        if (outputAudioStream is null) throw new VideoStreamException("Не удалось открыть поток записи аудио");

        if (inputAudioStream is not null) outputAudioStream->time_base = inputAudioStream->time_base;

        var codec = FFmpeg.Codec.FindEncoder(output->oformat->AudioCodec);
        if (codec is null) throw new VideoStreamException("Не удалось найти декодер формата для выходного аудиопотока");

        outputAudioCodecContext = FFmpeg.Codec.AllocContext3(codec);
        if (outputAudioCodecContext is null) throw new VideoStreamException("Не удалось выделить контекст кодека выходного аудиопотока");

        if (inputAudioStream is null || isDevice)
        {
            ChannelLayout cl = default;
            FFmpeg.Util.DefaultChannelLayout(&cl, audioChannels);

            outputAudioCodecContext->sample_rate = audioSampleRate;
            outputAudioCodecContext->ChannelLayout = cl;
            outputAudioCodecContext->SampleFormat = output->oformat->AudioCodec.ToSampleFormat();
            outputAudioCodecContext->time_base = new Ratio { num = 1, den = outputAudioCodecContext->sample_rate };
            outputAudioCodecContext->framerate = new Ratio { num = outputAudioCodecContext->sample_rate, den = 1 };
            outputAudioCodecContext->bit_rate = 256000;
            outputAudioCodecContext->rc_max_rate = 2500000;
            outputAudioCodecContext->trellis = 1;
            outputAudioCodecContext->qmax = 51;
            outputAudioCodecContext->gop_size = 12;
        }
        else
        {
            outputAudioCodecContext->sample_rate = inputAudioCodecContext->sample_rate;
            outputAudioCodecContext->ChannelLayout = inputAudioCodecContext->ChannelLayout;
            outputAudioCodecContext->SampleFormat = inputAudioCodecContext->SampleFormat;
            outputAudioCodecContext->time_base = inputAudioCodecContext->time_base;
            outputAudioCodecContext->bit_rate = inputAudioCodecContext->bit_rate;
            outputAudioCodecContext->rc_max_rate = inputAudioCodecContext->rc_max_rate;
            outputAudioCodecContext->trellis = inputAudioCodecContext->trellis;
            outputAudioCodecContext->qmax = inputAudioCodecContext->qmax;
            outputAudioCodecContext->gop_size = inputAudioCodecContext->gop_size;
        }

        FFmpeg.Codec.Open2(outputAudioCodecContext, codec, default)
            .ThrowIfErrors("Не удалось открыть кодек выходного формата аудиопотока");

        if (inputAudioStream is not null && !isDevice && inputAudioCodecContext->Kind == outputAudioCodecContext->Kind)
            FFmpeg.Codec.ParametersCopy(outputAudioStream->codecpar, inputAudioStream->codecpar)
                .ThrowIfErrors("Не удалось скопировать параметры выходного аудиопотока");
        else
            FFmpeg.Codec.ParametersFromContext(outputAudioStream->codecpar, outputAudioCodecContext)
                .ThrowIfErrors("Не удалось передать параметры в контекст выходного формата аудиопотока");

        outputAudioStreamIndex = outputAudioStream->index;
    }

    private void SetUpOutput()
    {
        isOutputReady = default;

        if (processingTask is not null)
        {
            cts.Cancel();
            processingTask.Wait();
            CloseOutput();
        }

        cts = new CancellationTokenSource();
        processingTask = Task.Run(() => Process(cts.Token));

        if (string.IsNullOrEmpty(outputPath) || outputPath is DevNull) return;

        unsafe
        {
            isDevice = outputPath.StartsWith("/dev/");
            var shortName = isDevice ? "v4l2" : default;
            var fileName = isDevice ? default : outputPath;

            var outputFormat = FFmpeg.Format.GuessFormat(shortName, fileName, default);
            if (outputFormat is null) throw new VideoStreamException("Не удалось угадать формат вывода");

            fixed (FormatContext** ctx = &output) FFmpeg.Format.AllocOutputContext2(ctx, outputFormat, default, outputPath)
                .ThrowIfErrors("Не удалось выделить память для контекста выходного формата");

            SetUpVideoOutput();
            SetUpAudioOutput();

            FFmpeg.Format.IoOpen(&output->pb, outputPath, 2).ThrowIfErrors("Не удалось открыть поток записи");

            output->max_index_size = 1 << 20;
            output->max_picture_buffer = 1 << 20;

            FFmpeg.Format.WriteHeader(output, default).ThrowIfErrors("Не удалось записать заголовок формата");
        }

        isOutputReady = true;
    }

    private unsafe void CloseInput()
    {
        fixed (CodecContext** ctx = &inputVideoCodecContext) if (ctx is not null) FFmpeg.Codec.FreeContext(ctx);
        fixed (CodecContext** ctx = &inputAudioCodecContext) if (ctx is not null) FFmpeg.Codec.FreeContext(ctx);
        fixed (FormatContext** ctx = &input) if (ctx is not null) FFmpeg.Format.CloseInput(ctx);
    }

    private unsafe void CloseOutput()
    {
        if (isOutputReady) FFmpeg.Format.WriteTrailer(output).ThrowIfErrors("Не удалось записать трейлер");
        if (output is not null && output->pb is not null) FFmpeg.Format.IoCloseP(&output->pb).ThrowIfErrors("Не удалось закрыть поток вывода");
        fixed (CodecContext** ctx = &outputVideoCodecContext) if (ctx is not null) FFmpeg.Codec.FreeContext(ctx);
        fixed (CodecContext** ctx = &outputAudioCodecContext) if (ctx is not null) FFmpeg.Codec.FreeContext(ctx);
        if (output is not null) FFmpeg.Format.FreeContext(output);
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

        CloseOutput();
        CloseInput();
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
    public void WaitForEnding(TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        neededFrames = (long)(frameRate * timeout.TotalSeconds);
        Wait.Until(() => !cts.IsCancellationRequested, TimeSpan.FromMilliseconds(50), timeout);
        readyFrames = default;
        var time = (int)(timeout - timer.Elapsed).TotalMilliseconds;
        if (time > 0) Thread.Sleep(time);
    }

    /// <summary>
    /// Ожидает завершение записи в выходной поток.
    /// </summary>
    public void WaitForEnding() => Wait.Until(() => !cts.IsCancellationRequested);

    /// <summary>
    /// Ожидает завершение записи в выходной поток.
    /// </summary>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public async ValueTask WaitForEndingAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        neededFrames = (long)(frameRate * timeout.TotalSeconds);
        await Wait.UntilAsync(() => !cts.IsCancellationRequested, TimeSpan.FromMilliseconds(50), timeout, cancellationToken).ConfigureAwait(false);
        readyFrames = default;
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
    public ValueTask WaitForEndingAsync(CancellationToken cancellationToken) => Wait.UntilAsync(() => !cts.IsCancellationRequested, cancellationToken);

    /// <summary>
    /// Ожидает завершение записи в выходной поток.
    /// </summary>
    public ValueTask WaitForEndingAsync() => WaitForEndingAsync(CancellationToken.None);
}