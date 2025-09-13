using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Text;
using Atom.Buffers;
using Atom.Media.Filters;
using Atom.Threading;

namespace Atom.Media;

/// <summary>
/// Представляет контекст медиа формата.
/// </summary>
public class VideoStream : Stream
{
    private const string DevNull = "/dev/null";

    private readonly Locker locker = new();

    private readonly unsafe FormatContext* input = null;
    private unsafe FormatContext* output = null;

    private int inputVideoStreamIndex = -1;
    private int inputAudioStreamIndex = -1;

    private unsafe CodecContext* videoDecoder;
    private unsafe CodecContext* audioDecoder;

    private int outputVideoStreamIndex = -1;
    private int outputAudioStreamIndex = -1;

    private unsafe CodecContext* videoEncoder = null;
    private unsafe CodecContext* audioEncoder = null;

    private unsafe void* filterGraph;
    private unsafe MediaFilterInOut* filterInputs;
    private unsafe MediaFilterInOut* filterOutputs;

    private readonly unsafe void* bufferSourceContext;
    private readonly unsafe void* bufferSinkContext;

    private Task? processingTask;
    private CancellationTokenSource cts;
    private bool isInputReady;
    private bool isOutputReady;
    private bool isDevice;
    private bool isImageInput;

    private Size resolution;
    private int frameRate;
    private int audioSampleRate;
    private int audioChannels;

    private bool isDisposed;

    private string inputPath;
    private string outputPath;

    private string previousInputPath;

    private long readyFrames;
    private long neededFrames;

    private long videoPts;
    private long audioPts;

    private long length;

    private readonly List<IFilter> filters = [];

    /// <summary>
    /// Указывает, был ли закрыт медиа вход.
    /// </summary>
    public bool IsClosed { get; protected set; }

    /// <inheritdoc/>
    public override unsafe bool CanRead => input is not null && !string.IsNullOrEmpty(inputPath) && inputPath is not DevNull && isInputReady;

    /// <inheritdoc/>
    public override bool CanSeek { get; } = true;

    /// <inheritdoc/>
    public override unsafe bool CanWrite => output is not null && !string.IsNullOrEmpty(outputPath) && outputPath is not DevNull && isOutputReady;

    /// <inheritdoc/>
    public override long Length => length;

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
            previousInputPath = inputPath;
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
            if (outputPath == value) return;
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
    /// Режим масштабирования.
    /// </summary>
    public ScaleMode ScaleMode { get; set; }

    /// <summary>
    /// Коллекция фильтров.
    /// </summary>
    public IEnumerable<IFilter> Filters
    {
        get => filters;

        set
        {
            locker.Wait();

            filters.Clear();
            filters.AddRange(value ?? []);
            SetUpFilters();

            locker.Release();
        }
    }

    /// <summary>
    /// Определяет, является ли запись в поток активной.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VideoStream"/>.
    /// </summary>
    /// <param name="inputPath">Путь к файлу видео.</param>
    /// <param name="outputPath">Путь к видеоустройству.</param>
    /// <param name="resolution">Разрешение видео.</param>
    public VideoStream(string inputPath, string outputPath, Size resolution)
    {
        previousInputPath = inputPath;
        this.inputPath = inputPath;
        this.outputPath = outputPath;
        this.resolution = resolution;

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

        if (inputVideoStreamIndex < 0) return;

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

        if (resolution == default) resolution = new Size(videoDecoder->width, videoDecoder->height);

        isImageInput = inputStream->nb_frames < 2 && input->duration <= 1000 && inputStream->codecpar->Kind is not MediaCodec.MJPEG and not MediaCodec.BMP and not MediaCodec.PNG;

        SetUpVideoEncoder();
    }

    private unsafe void SetUpAudioDecoder()
    {
        if (IsMuted) return;

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

        if (inputAudioStreamIndex < 0) return;

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

        SetUpAudioEncoder();
    }

    private void SetUpDecoder()
    {
        isInputReady = default;

        if (processingTask is not null)
        {
            cts.Cancel();
            processingTask.Wait();
            processingTask = default;
        }

        locker.Wait();

        inputVideoStreamIndex = -1;
        inputAudioStreamIndex = -1;

        CloseDecoder();

        cts = new CancellationTokenSource();
        processingTask = Task.Run(async () => await ProcessAsync(cts.Token).ConfigureAwait(false));

        if (string.IsNullOrEmpty(inputPath) || inputPath is DevNull)
        {
            locker.Release();
            return;
        }

        unsafe
        {
            fixed (FormatContext** ctx = &input)
            {
                FFmpeg.Format.OpenInput(ctx, inputPath, default, default)
                .ThrowIfErrors("Не удалось инициализировать контекст входного формата", locker);
            }

            FFmpeg.Format.FindStreamInfo(input, default).ThrowIfErrors("Не удалось найти информацию о входном потоке", locker);

            SetUpVideoDecoder();
            SetUpAudioDecoder();

            if (videoDecoder is null && audioDecoder is null)
            {
                locker.Release();
                throw new VideoStreamException("Не удалось найти ни одного аудио и видео потока");
            }

            if (resolution == default && videoDecoder is not null) resolution = new Size(videoDecoder->width, videoDecoder->height);
        }

        isInputReady = true;
        locker.Release();
    }

    private unsafe void SetUpVideoEncoder()
    {
        fixed (CodecContext** ctx = &videoEncoder)
        {
            if (ctx is not null)
            {
                FFmpeg.Codec.Close(videoEncoder);
                FFmpeg.Codec.FreeContext(ctx);
                videoEncoder = default;
            }
        }

        if (output is null || output->oformat->VideoCodec is MediaCodec.NONE) return;

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
            videoEncoder->gop_size = 12;
            videoEncoder->max_b_frames = 2;
        }
        else
        {
            if (videoDecoder->bit_rate > 0) videoEncoder->bit_rate = videoDecoder->bit_rate;
            if (videoDecoder->rc_buffer_size > 0) videoEncoder->rc_buffer_size = videoDecoder->rc_buffer_size;
            if (videoDecoder->rc_min_rate > 0) videoEncoder->rc_min_rate = videoDecoder->rc_min_rate;
            if (videoDecoder->rc_max_rate > 0) videoEncoder->rc_max_rate = videoDecoder->rc_max_rate;
            if (videoDecoder->gop_size > 0) videoEncoder->gop_size = videoDecoder->gop_size;
            if (videoDecoder->max_b_frames > 0) videoEncoder->max_b_frames = videoDecoder->max_b_frames;
            if (videoDecoder->flags > 0) videoEncoder->flags = videoDecoder->flags;
            if (videoDecoder->sample_aspect_ratio.num > 0) videoEncoder->sample_aspect_ratio = videoDecoder->sample_aspect_ratio;
        }

        videoEncoder->PixelFormat = videoEncoder->Kind.ToPixelFormat();
        videoEncoder->width = (int)Math.Ceiling((double)resolution.Width / 16) * 16;
        videoEncoder->height = (int)Math.Ceiling((double)resolution.Height / 16) * 16;
        resolution = new Size(videoEncoder->width, videoEncoder->height);

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
    }

    private unsafe void SetUpAudioEncoder()
    {
        if (IsMuted) return;

        fixed (CodecContext** ctx = &audioEncoder)
        {
            if (ctx is not null)
            {
                FFmpeg.Codec.Close(audioEncoder);
                FFmpeg.Codec.FreeContext(ctx);
                audioEncoder = default;
            }
        }

        if (output is null || output->oformat->AudioCodec is MediaCodec.NONE) return;

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
    }

    private void SetUpEncoder()
    {
        readyFrames = default;

        if (processingTask is not null)
        {
            cts.Cancel();
            processingTask.Wait();
            processingTask = default;
        }

        locker.Wait();

        outputVideoStreamIndex = -1;
        outputAudioStreamIndex = -1;
        videoPts = default;
        audioPts = default;

        CloseEncoder();

        isOutputReady = default;
        cts = new CancellationTokenSource();
        processingTask = Task.Run(async () => await ProcessAsync(cts.Token).ConfigureAwait(false));

        if (string.IsNullOrEmpty(outputPath) || outputPath is DevNull)
        {
            locker.Release();
            return;
        }

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
        locker.Release();
    }

    private unsafe void SetUpFilters()
    {
        CloseFilters();

        if (filters.Count is 0) return;

        filterGraph = FFmpeg.Filter.GraphAlloc();

        if (filterGraph is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось выделить память для графа фильтров");
        }

        var sb = ObjectPool<StringBuilder>.Shared.Rent();

        foreach (var filter in filters)
        {
            if (filter is VideoFilter f)
            {
                f.FrameRate = frameRate;
                f.Resolution = resolution;
            }

            sb.Append(filter.Calculate()).Append(',');
        }

        sb.Append($"scale={resolution.Width}:{resolution.Height}");

        var filterSpec = sb.ToString();
        ObjectPool<StringBuilder>.Shared.Return(sb, x => x.Clear());

        var format = PixelFormat.YUV420P;
        var timeBase = new Ratio { den = 1, num = frameRate };
        var sampleAspectRatio = timeBase;

        if (videoEncoder is not null)
        {
            format = videoEncoder->PixelFormat;
            timeBase = videoEncoder->time_base;
            sampleAspectRatio = videoEncoder->sample_aspect_ratio;
        }

        var bufferSourceArgs = $"video_size={resolution.Width}x{resolution.Height}:pix_fmt={Enum.GetName(format)?.ToLowerInvariant() ?? "yuv420p"}:time_base={timeBase.num}/{timeBase.den}:pixel_aspect={sampleAspectRatio.num}/{sampleAspectRatio.den}";

        var bufferSource = FFmpeg.Filter.GetByName("buffer");
        var bufferSink = FFmpeg.Filter.GetByName("buffersink");

        fixed (void** ctx = &bufferSourceContext) FFmpeg.Filter.CreateInGraph(ctx, bufferSource, "in", bufferSourceArgs, default, filterGraph).ThrowIfErrors("Не удалось инициализировать вход графа фильтров", locker);
        fixed (void** ctx = &bufferSinkContext) FFmpeg.Filter.CreateInGraph(ctx, bufferSink, "out", default, default, filterGraph).ThrowIfErrors("Не удалось инициализировать выход графа фильтров", locker);

        filterOutputs = FFmpeg.Filter.InOutAlloc();
        filterOutputs->name = FFmpeg.Util.StrDup("in");
        filterOutputs->filter_ctx = bufferSourceContext;
        filterOutputs->pad_idx = 0;
        filterOutputs->next = null;

        filterInputs = FFmpeg.Filter.InOutAlloc();
        filterInputs->name = FFmpeg.Util.StrDup("out");
        filterInputs->filter_ctx = bufferSinkContext;
        filterInputs->pad_idx = 0;
        filterInputs->next = null;

        fixed (MediaFilterInOut** inputs = &filterInputs)
        fixed (MediaFilterInOut** outputs = &filterOutputs)
            FFmpeg.Filter.ParseGraphSpecs(filterGraph, filterSpec, inputs, outputs, default).ThrowIfErrors("Не удалось спарсить спецификации графа фильтров", locker);

        FFmpeg.Filter.GraphConfig(filterGraph, default).ThrowIfErrors("Не удалось сконфигурировать граф фильтров", locker);
    }

    private unsafe MediaFrame* ScaleStretch(MediaFrame* frame, PixelFormat format, int width, int height)
    {
        var convertedFrame = FFmpeg.Util.FrameAlloc();

        if (convertedFrame is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось выделить память для преобразованного фрейма");
        }

        convertedFrame->width = width;
        convertedFrame->height = height;
        convertedFrame->Format = (int)format;

        FFmpeg.Util.FrameGetBuffer(convertedFrame, 32).ThrowIfErrors("Ошибка выделения буфера для преобразованного фрейма", locker);
        FFmpeg.Util.FrameMakeWritable(convertedFrame).ThrowIfErrors("Не удалось сделать фрейм записываемым", locker);

        var swsContext = FFmpeg.SwScale.GetContext(
            frame->width, frame->height, (PixelFormat)frame->Format,
            width, height, format,
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
        return convertedFrame;
    }

    private unsafe MediaFrame* ScaleCover(MediaFrame* frame, PixelFormat format, int width, int height)
    {
        var srcAspect = (double)frame->width / frame->height;
        var dstAspect = (double)width / height;

        int scaledWidth, scaledHeight;
        int xOffset, yOffset;

        if (srcAspect > dstAspect)
        {
            scaledHeight = height;
            scaledWidth = (int)(height * srcAspect);
            xOffset = (width - scaledWidth) / 2;
            yOffset = 0;
        }
        else
        {
            scaledWidth = width;
            scaledHeight = (int)(width / srcAspect);
            xOffset = 0;
            yOffset = (height - scaledHeight) / 2;
        }

        var swsContext = FFmpeg.SwScale.GetContext(
            frame->width, frame->height, (PixelFormat)frame->Format,
            scaledWidth, scaledHeight, format,
            2, default, default, default
        );

        if (swsContext is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось создать контекст масштабирования фрейма");
        }

        var scaledFrame = FFmpeg.Util.FrameAlloc();

        if (scaledFrame is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось выделить память для преобразованного фрейма");
        }

        scaledFrame->width = scaledWidth;
        scaledFrame->height = scaledHeight;
        scaledFrame->Format = (int)format;

        FFmpeg.Util.FrameGetBuffer(scaledFrame, 32).ThrowIfErrors("Ошибка выделения буфера для преобразованного фрейма", locker);
        FFmpeg.Util.FrameMakeWritable(scaledFrame).ThrowIfErrors("Не удалось сделать фрейм записываемым", locker);

        FFmpeg.SwScale.Scale(
            swsContext,
            frame->data.Source,
            frame->linesize,
            0,
            frame->height,
            scaledFrame->data.Source,
            scaledFrame->linesize
        );

        var finalFrame = FFmpeg.Util.FrameAlloc();

        if (finalFrame is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось выделить память для преобразованного фрейма");
        }

        finalFrame->width = width;
        finalFrame->height = height;
        finalFrame->Format = (int)format;

        FFmpeg.Util.FrameGetBuffer(finalFrame, 32).ThrowIfErrors("Ошибка выделения буфера для преобразованного фрейма", locker);
        FFmpeg.Util.FrameMakeWritable(finalFrame).ThrowIfErrors("Не удалось сделать фрейм записываемым", locker);

        for (var y = 0; y < scaledHeight; ++y)
        {
            for (var x = 0; x < scaledWidth; ++x)
            {
                finalFrame->data[0][((y + yOffset) * finalFrame->linesize[0]) + x + xOffset] = scaledFrame->data[0][(y * scaledFrame->linesize[0]) + x];
            }
        }

        FFmpeg.SwScale.Scale(
            swsContext,
            frame->data.Source,
            frame->linesize,
            0,
            frame->height,
            finalFrame->data.Source,
            finalFrame->linesize
        ).ThrowIfErrors("Не удалось отмасштабировать фрейм", locker);

        FFmpeg.SwScale.FreeContext(swsContext);
        FFmpeg.Util.FrameFree(&scaledFrame);

        return finalFrame;
    }

    private unsafe MediaFrame* ScaleFit(MediaFrame* frame, PixelFormat format, int width, int height)
    {
        var srcAspect = (double)frame->width / frame->height;
        var dstAspect = (double)width / height;
        int scaledWidth, scaledHeight;
        int xOffset, yOffset;

        if (srcAspect > dstAspect)
        {
            scaledWidth = width;
            scaledHeight = (int)(width / srcAspect);
            xOffset = 0;
            yOffset = (scaledHeight - height) / 2;
        }
        else
        {
            scaledHeight = height;
            scaledWidth = (int)(height * srcAspect);
            xOffset = (scaledWidth - width) / 2;
            yOffset = 0;
        }

        var swsContext = FFmpeg.SwScale.GetContext(
            frame->width, frame->height, (PixelFormat)frame->Format,
            scaledWidth, scaledHeight, format,
            2, default, default, default
        );

        var scaledFrame = FFmpeg.Util.FrameAlloc();

        if (scaledFrame is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось выделить память для преобразованного фрейма");
        }

        scaledFrame->width = scaledWidth;
        scaledFrame->height = scaledHeight;
        scaledFrame->Format = (int)format;

        FFmpeg.Util.FrameGetBuffer(scaledFrame, 32).ThrowIfErrors("Ошибка выделения буфера для преобразованного фрейма", locker);
        FFmpeg.Util.FrameMakeWritable(scaledFrame).ThrowIfErrors("Не удалось сделать фрейм записываемым", locker);

        FFmpeg.SwScale.Scale(
            swsContext,
            frame->data.Source,
            frame->linesize,
            0,
            frame->height,
            scaledFrame->data.Source,
            scaledFrame->linesize
        );

        var finalFrame = FFmpeg.Util.FrameAlloc();

        if (finalFrame == null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось выделить память для преобразованного фрейма");
        }

        finalFrame->width = width;
        finalFrame->height = height;
        finalFrame->Format = (int)format;

        FFmpeg.Util.FrameGetBuffer(finalFrame, 32).ThrowIfErrors("Ошибка выделения буфера для преобразованного фрейма", locker);
        FFmpeg.Util.FrameMakeWritable(finalFrame).ThrowIfErrors("Не удалось сделать фрейм записываемым", locker);

        for (var y = 0; y < height; ++y)
        {
            for (var x = 0; x < width; ++x)
            {
                finalFrame->data[0][(y * finalFrame->linesize[0]) + x] = scaledFrame->data[0][((y + yOffset) * scaledFrame->linesize[0]) + x + xOffset];
            }
        }

        FFmpeg.SwScale.Scale(
            swsContext,
            frame->data.Source,
            frame->linesize,
            0,
            frame->height,
            finalFrame->data.Source,
            finalFrame->linesize
        ).ThrowIfErrors("Не удалось отмасштабировать фрейм", locker);

        FFmpeg.SwScale.FreeContext(swsContext);
        FFmpeg.Util.FrameFree(&scaledFrame);

        return finalFrame;
    }

    private unsafe MediaFrame* Filter(MediaFrame* frame)
    {
        if (filterGraph is null) return frame;

        var filteredFrame = FFmpeg.Util.FrameAlloc();

        if (filteredFrame is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось выделить память для фрейма фильтра");
        }

        filteredFrame->width = frame->width;
        filteredFrame->height = frame->height;
        filteredFrame->Format = frame->Format;

        FFmpeg.Util.FrameGetBuffer(filteredFrame, 32).ThrowIfErrors("Ошибка выделения буфера для преобразованного фрейма", locker);
        FFmpeg.Util.FrameMakeWritable(filteredFrame).ThrowIfErrors("Не удалось сделать фрейм записываемым", locker);

        FFmpeg.Util.FrameCopy(filteredFrame, frame).ThrowIfErrors("Не удалось скопировать данные фрейма фильтра", locker);

        FFmpeg.Filter.AddFrameFlagsToContext(bufferSourceContext, filteredFrame, 0).ThrowIfErrors("Не удалось установить флаги фрейму фильтра", locker);
        FFmpeg.Filter.GetFrameBufferSink(bufferSinkContext, filteredFrame).ThrowIfErrors("Не удалось получить буфер фрейма фильтра", locker);

        return filteredFrame;
    }

    private unsafe bool Encode(CodecContext* encoder, MediaStream* inputStream, MediaStream* outputStream, MediaFrame* frame, int samples, Ratio inputTimeBase, Ratio outputTimeBase)
    {
        if (neededFrames > 0 && readyFrames > neededFrames) return default;

        var filteredFrame = Filter(frame);

        filteredFrame->pts = inputStream->index == inputVideoStreamIndex ? videoPts : audioPts;
        filteredFrame->pict_type = (videoPts is 0) ? 1 : 2;
        filteredFrame->flags = 0;
        filteredFrame->duration = outputTimeBase.den / frameRate;
        filteredFrame->time_base = outputTimeBase;

        ++length;

        const string packetError = "Не удалось освободить пакет";
        var packet = FFmpeg.Codec.PacketAlloc();
        FFmpeg.Codec.SendFrame(encoder, filteredFrame).ThrowIfErrors("Не удалось отправить фрейм в энкодер", locker);

        var result = FFmpeg.Codec.ReceivePacket(encoder, packet);

        if (isDevice)
        {
            packet->pts = packet->dts = filteredFrame->pts;
            packet->stream_index = outputStream->index;

            if (packet->stream_index == inputVideoStreamIndex)
                ++videoPts;
            else
                audioPts += samples;
        }
        else
        {
            if (packet->stream_index == inputVideoStreamIndex)
                videoPts += filteredFrame->duration;
            else
                audioPts += FFmpeg.Util.ReScaleQ(samples, inputTimeBase, outputTimeBase);
        }

        if (result is -11 or -541_478_725)
        {
            FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors(packetError);
            FFmpeg.Codec.PacketFree(&packet);
            if (filteredFrame != frame) FFmpeg.Util.FrameFree(&filteredFrame);

            locker.Release();
            Thread.Sleep(10);
            locker.Wait();

            return true;
        }

        result.ThrowIfErrors("Не удалось получить пакет из энкодера", locker);

        FFmpeg.Format.InterleavedWriteFrame(output, packet).ThrowIfErrors("Не удалось записать фрейм", locker);
        if (packet->stream_index == inputVideoStreamIndex) ++readyFrames;

        FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors(packetError, locker);
        FFmpeg.Codec.PacketFree(&packet);
        if (filteredFrame != frame) FFmpeg.Util.FrameFree(&filteredFrame);

        if (isDevice)
        {
            locker.Release();
            Thread.Sleep(1000 / frameRate);
            locker.Wait();
        }

        return true;
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

            locker.Release();
            Thread.Sleep(10);
            locker.Wait();

            return default;
        }

        result.ThrowIfErrors("Не удалось отправить пакет на декодер", locker);
        result = FFmpeg.Codec.ReceiveFrame(decoder, frame);

        if (result is -11 or -541_478_725)
        {
            FFmpeg.Util.FrameFree(&frame);

            locker.Release();
            Thread.Sleep(10);
            locker.Wait();

            return default;
        }

        result.ThrowIfErrors("Не удалось получить фрейм из декодера", locker);

        var pts = packet->stream_index == inputVideoStreamIndex ? videoPts : audioPts;

        if (packet->stream_index == inputVideoStreamIndex && (frame->Format != encoderFormat || frame->width != encoderWidth || frame->height != encoderHeight))
        {
            var convertedFrame = ScaleMode switch
            {
                ScaleMode.Fit => ScaleFit(frame, (PixelFormat)encoderFormat, encoderWidth, encoderHeight),
                ScaleMode.Cover => ScaleCover(frame, (PixelFormat)encoderFormat, encoderWidth, encoderHeight),
                _ => ScaleStretch(frame, (PixelFormat)encoderFormat, encoderWidth, encoderHeight),
            };

            if (convertedFrame == null)
            {
                locker.Release();
                throw new VideoStreamException("Не удалось выделить память для преобразованного фрейма");
            }

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

        if (!IsActive)
        {
            locker.Release();
            Thread.Sleep(10);
            locker.Wait();

            return true;
        }

        var packet = FFmpeg.Codec.PacketAlloc();

        if (packet is null)
        {
            locker.Release();
            throw new VideoStreamException("Не удалось выделить память для пакета");
        }

        const string packetError = "Не удалось освободить пакет";

        if (FFmpeg.Format.ReadFrame(input, packet) < 0)
        {
            if (IsLooped || readyFrames < neededFrames)
            {
                FFmpeg.Format.SeekFrame(input, -1, 0, 1).ThrowIfErrors("Ошибка перемещения указателя на начало", locker);
                if (videoDecoder is not null) FFmpeg.Codec.FlushBuffers(videoDecoder);
                if (audioDecoder is not null) FFmpeg.Codec.FlushBuffers(audioDecoder);
            }

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
            if (isImageInput)
            {
                locker.Release();

                while (CanRead)
                {
                    if (!IsActive || !CanWrite)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    if (!string.IsNullOrEmpty(previousInputPath) && previousInputPath != inputPath) break;

                    locker.Wait();

                    if (!Encode(encoder, inputStream, outputStream, frame, frame->nb_samples, inputStream->time_base, outputStream->time_base) || (output is not null && output->oformat->IsImage && readyFrames > 0))
                    {
                        locker.Release();
                        break;
                    }

                    locker.Release();
                }

                locker.Wait();

                FFmpeg.Util.FrameFree(&frame);
                FFmpeg.Codec.UnRefPacket(packet).ThrowIfErrors(packetError, locker);
                FFmpeg.Codec.PacketFree(&packet);

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

        if (!IsActive)
        {
            locker.Release();
            Thread.Sleep(10);
            locker.Wait();

            return true;
        }

        if (videoEncoder is not null) WhiteNoiseGenerator.WriteVideoFrame(videoEncoder, output, outputVideoStreamIndex, ref videoPts, locker);
        if (!output->oformat->IsImage && !IsMuted && audioEncoder is not null) WhiteNoiseGenerator.WriteAudioFrame(audioEncoder, output, outputAudioStreamIndex, ref audioPts, locker);

        ++readyFrames;
        if (neededFrames <= 0 || isDevice) Thread.Sleep(1000 / frameRate);

        return neededFrames <= 0 || readyFrames <= neededFrames;
    }

    private async ValueTask ProcessAsync(CancellationToken cancellationToken)
    {
        await Wait.UntilAsync(() => CanWrite || cancellationToken.IsCancellationRequested || isDisposed, cancellationToken).ConfigureAwait(false);

        while (CanWrite && !cancellationToken.IsCancellationRequested && !Volatile.Read(ref isDisposed))
        {
            await locker.WaitAsync(CancellationToken.None).ConfigureAwait(false);

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

        if (!cancellationToken.IsCancellationRequested) await cts.CancelAsync().ConfigureAwait(false);
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

        if (output is not null)
        {
            FFmpeg.Format.FreeContext(output);
            output = default;
        }
    }

    private unsafe void CloseFilters()
    {
        fixed (void** ctx = &filterGraph) if (ctx is not null) FFmpeg.Filter.GraphFree(ctx);
        fixed (MediaFilterInOut** ctx = &filterOutputs) if (ctx is not null) FFmpeg.Filter.InOutFree(ctx);
        fixed (MediaFilterInOut** ctx = &filterInputs) if (ctx is not null) FFmpeg.Filter.InOutFree(ctx);
    }

    /// <summary>
    /// Освобождает неуправляемые ресурсы, используемые объектом.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;

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
        CloseFilters();
    }

    /// <inheritdoc/>
    public override void Flush() => throw new NotSupportedException();

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override unsafe long Seek(long offset, SeekOrigin origin)
    {
        locker.Wait();
        FFmpeg.Format.SeekFrame(input, -1, 0, 1).ThrowIfErrors("Ошибка перемещения указателя на начало", locker);
        locker.Release();
        return offset;
    }

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
        neededFrames = (long)(frameRate * timeout.TotalSeconds);
        IsActive = true;

        var timer = Stopwatch.StartNew();

        Wait.Until(() => cts.IsCancellationRequested || (!IsLooped && readyFrames >= neededFrames), timeout);

        if (output->oformat->IsImage || !isDevice)
        {
            IsActive = default;
            return;
        }

        var time = (int)(timeout - timer.Elapsed).TotalMilliseconds;
        if (time > 0) Thread.Sleep(time);

        IsActive = default;
    }

    /// <summary>
    /// Ожидает завершение записи в выходной поток.
    /// </summary>
    public void WaitForEnding()
    {
        IsActive = true;
        Wait.Until(() => cts.IsCancellationRequested || (!IsLooped && readyFrames >= neededFrames));
        IsActive = default;
    }

    /// <summary>
    /// Ожидает завершение записи в выходной поток.
    /// </summary>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public async ValueTask WaitForEndingAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        readyFrames = default;
        neededFrames = (long)(frameRate * timeout.TotalSeconds);
        IsActive = true;

        var timer = Stopwatch.StartNew();

        await Wait.UntilAsync(() => cts.IsCancellationRequested || (!IsLooped && readyFrames >= neededFrames), timeout, cancellationToken).ConfigureAwait(false);

        unsafe
        {
            if (output->oformat->IsImage || !isDevice)
            {
                IsActive = default;
                return;
            }
        }

        var time = (int)(timeout - timer.Elapsed).TotalMilliseconds;
        if (time > 0) await Task.Delay(time, cancellationToken).ConfigureAwait(false);

        IsActive = default;
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
        IsActive = true;
        await Wait.UntilAsync(() => cts.IsCancellationRequested || (!IsLooped && readyFrames >= neededFrames), cancellationToken).ConfigureAwait(false);
        IsActive = default;
    }

    /// <summary>
    /// Ожидает завершение записи в выходной поток.
    /// </summary>
    public ValueTask WaitForEndingAsync() => WaitForEndingAsync(CancellationToken.None);
}