using Atom.Media.Video.Backends;

namespace Atom.Media.Video;

/// <summary>
/// Представляет устройство виртуальной камеры.
/// Кроссплатформенно создаёт виртуальное устройство камеры
/// и транслирует в него видеопоток.
/// </summary>
/// <remarks>
/// <para>
/// Создание камеры выполняется через фабричный метод <see cref="CreateAsync(VirtualCameraSettings, CancellationToken)"/>.
/// Фреймы записываются через <see cref="WriteFrame(ReadOnlySpan{byte})"/>.
/// </para>
/// <para>
/// На Linux используется нативный PipeWire API — камера создаётся
/// как PipeWire video source нода без root-прав.
/// </para>
/// <example>
/// <code>
/// var settings = new VirtualCameraSettings { Width = 1920, Height = 1080 };
/// await using var camera = await VirtualCamera.CreateAsync(settings);
///
/// await camera.StartCaptureAsync();
/// camera.WriteFrame(rawFrameData);
/// await camera.StopCaptureAsync();
/// </code>
/// </example>
/// </remarks>
public sealed class VirtualCamera : IAsyncDisposable
{
    private readonly IVirtualCameraBackend backend;
    private bool isDisposed;

    private int ExpectedFrameSize => Settings.PixelFormat.CalculateFrameSize(Settings.Width, Settings.Height);

    /// <summary>
    /// Настройки камеры.
    /// </summary>
    public VirtualCameraSettings Settings { get; }

    /// <summary>
    /// Идентификатор виртуального устройства камеры в системе.
    /// </summary>
    public string DeviceIdentifier => backend.DeviceIdentifier;

    /// <summary>
    /// Определяет, активен ли захват видеопотока.
    /// </summary>
    public bool IsCapturing => backend.IsCapturing;

    private VirtualCamera(IVirtualCameraBackend backend, VirtualCameraSettings settings)
    {
        this.backend = backend;
        Settings = settings;
    }

    /// <summary>
    /// Начинает захват видеопотока.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public ValueTask StartCaptureAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        return backend.StartCaptureAsync(cancellationToken);
    }

    /// <summary>
    /// Начинает захват видеопотока.
    /// </summary>
    public ValueTask StartCaptureAsync() => StartCaptureAsync(CancellationToken.None);

    /// <summary>
    /// Записывает сырые данные видеокадра в виртуальную камеру.
    /// </summary>
    /// <param name="frameData">
    /// Данные кадра. Для packed форматов (RGB, BGRA) — единый буфер.
    /// Для planar форматов (YUV420P) — все плоскости последовательно (Y, U, V).
    /// </param>
    public void WriteFrame(ReadOnlySpan<byte> frameData)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        if (frameData.IsEmpty)
        {
            throw new ArgumentException("Данные кадра не могут быть пустыми.", nameof(frameData));
        }

        var expectedFrameSize = ExpectedFrameSize;
        if (expectedFrameSize > 0 && frameData.Length != expectedFrameSize)
        {
            throw new ArgumentException(
                string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Некорректный размер кадра: ожидалось {expectedFrameSize}, получено {frameData.Length}."),
                nameof(frameData));
        }

        backend.WriteFrame(frameData);
    }

    /// <summary>
    /// Записывает видеокадр из буфера в виртуальную камеру.
    /// </summary>
    /// <param name="buffer">Буфер видеокадра.</param>
    public void WriteFrame(VideoFrameBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        backend.WriteFrame(buffer.GetRawData());
    }

    /// <summary>
    /// Записывает текущий кадр из media stream в виртуальную камеру.
    /// </summary>
    /// <param name="mediaStream">Медиа-поток с уже декодированным видеокадром.</param>
    public void WriteFrame(MediaStream mediaStream)
    {
        ArgumentNullException.ThrowIfNull(mediaStream);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        if (mediaStream is not VideoStream videoStream)
        {
            throw new NotSupportedException($"VirtualCamera не поддерживает MediaStream типа '{mediaStream.StreamType}'.");
        }

        if (!videoStream.HasFrame)
        {
            throw new InvalidOperationException("В VideoStream ещё нет декодированного кадра.");
        }

        WriteFrame(videoStream.CurrentFrame.Span);
    }

    /// <summary>
    /// Декодирует изображение из файла и записывает его как видеокадр.
    /// Размеры изображения должны совпадать с настройками камеры.
    /// Поддерживаемые форматы: PNG, WebP.
    /// </summary>
    /// <param name="imagePath">Путь к файлу изображения.</param>
    /// <exception cref="NotSupportedException">Формат файла не поддерживается.</exception>
    public void WriteFrame(string imagePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(imagePath);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        var data = File.ReadAllBytes(imagePath);
        var extension = Path.GetExtension(imagePath);

        WriteFrameCore(data, extension);
    }

    /// <summary>
    /// Декодирует изображение из потока и записывает его как видеокадр.
    /// Размеры изображения должны совпадать с настройками камеры.
    /// Поддерживаемые форматы: PNG, WebP.
    /// </summary>
    /// <param name="stream">Поток с данными изображения.</param>
    /// <param name="format">Расширение формата изображения (например, ".png" или ".webp").</param>
    /// <exception cref="NotSupportedException">Формат не поддерживается.</exception>
    public void WriteFrame(Stream stream, string format)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrEmpty(format);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        using var ms = new MemoryStream();
        stream.CopyTo(ms);

        WriteFrameCore(ms.GetBuffer().AsSpan(0, (int)ms.Length), format);
    }

    private void WriteFrameCore(ReadOnlySpan<byte> data, string extension)
    {
        if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
        {
            var streamParameters = new VideoCodecParameters
            {
                Width = Settings.Width,
                Height = Settings.Height,
                PixelFormat = Settings.PixelFormat,
                FrameRate = Settings.FrameRate,
            };

            using var videoStream = VideoStream.FromStillImage(data, extension, streamParameters);
            WriteFrame(videoStream);
            return;
        }

        using var codec = CreateImageCodec(extension);
        var parameters = new ImageCodecParameters(
            Settings.Width, Settings.Height, Settings.PixelFormat);

        codec.InitializeDecoder(parameters)
            .ThrowIfError("Не удалось инициализировать декодер изображения.");

        using var frameBuffer = new VideoFrameBuffer(
            Settings.Width, Settings.Height, Settings.PixelFormat);

        var frame = frameBuffer.AsFrame();
        codec.Decode(data, ref frame)
            .ThrowIfError("Не удалось декодировать изображение.");

        backend.WriteFrame(frameBuffer.GetRawData());
    }

    /// <summary>
    /// Стримит кадры из media stream в виртуальную камеру.
    /// </summary>
    /// <param name="mediaStream">Медиа-поток, который должен поставлять видеокадры.</param>
    /// <param name="loop">Если true, одиночный кадр или поток воспроизводятся циклически.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public Task StreamFromAsync(MediaStream mediaStream, bool loop = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaStream);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        if (mediaStream is not VideoStream videoStream)
        {
            throw new NotSupportedException($"VirtualCamera не поддерживает MediaStream типа '{mediaStream.StreamType}'.");
        }

        return StreamFromCoreAsync(videoStream, loop, cancellationToken);
    }

    /// <summary>
    /// Стримит видео из медиафайла, покадрово декодируя и записывая в виртуальную камеру.
    /// Поддерживаемые контейнеры: MP4, MKV, WebM, AVI, MOV и другие зарегистрированные в <see cref="ContainerFactory"/>.
    /// </summary>
    /// <param name="filePath">Путь к видеофайлу.</param>
    /// <param name="loop">Если true, воспроизведение зацикливается.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public Task StreamFromAsync(string filePath, bool loop = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        var extension = Path.GetExtension(filePath);
        var formatName = ContainerFactory.GetFormatFromExtension(extension)
            ?? throw new NotSupportedException(
                "Формат контейнера '" + extension + "' не поддерживается.");

        return StreamFromFileCoreAsync(formatName, filePath, loop, cancellationToken);
    }

    /// <summary>
    /// Стримит видео из потока, покадрово декодируя и записывая в виртуальную камеру.
    /// </summary>
    /// <param name="stream">Поток с видеоданными.</param>
    /// <param name="format">Формат контейнера (например, "mp4", "webm", "matroska") или расширение файла (".mp4", ".mkv").</param>
    /// <param name="loop">Если true, воспроизведение зацикливается.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public Task StreamFromAsync(Stream stream, string format, bool loop = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrEmpty(format);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        var formatName = (format.StartsWith('.') ? ContainerFactory.GetFormatFromExtension(format) : format)
            ?? throw new NotSupportedException(
                "Формат контейнера '" + format + "' не поддерживается.");

        return StreamFromContainerStreamCoreAsync(formatName, stream, loop, cancellationToken);
    }

    /// <summary>
    /// Стримит видео из URL, покадрово декодируя и записывая в виртуальную камеру.
    /// Формат определяется по расширению URL.
    /// </summary>
    /// <param name="url">URL видеофайла.</param>
    /// <param name="httpClient">HTTP-клиент для загрузки. Если null, создаётся временный.</param>
    /// <param name="loop">Если true, воспроизведение зацикливается.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async Task StreamFromAsync(Uri url, HttpClient? httpClient = null, bool loop = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        if (httpClient is not null)
        {
            await StreamFromUrlCoreAsync(url, httpClient, loop, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var ownedClient = new HttpClient();
        await StreamFromUrlCoreAsync(url, ownedClient, loop, cancellationToken).ConfigureAwait(false);
    }

    private async Task StreamFromUrlCoreAsync(Uri url, HttpClient httpClient, bool loop, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(url.LocalPath);
        var formatName = ContainerFactory.GetFormatFromExtension(extension)
            ?? throw new NotSupportedException(
                "Формат контейнера '" + extension + "' не поддерживается.");

        var stream = await httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            await StreamFromContainerStreamCoreAsync(formatName, stream, loop, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task StreamFromFileCoreAsync(
        string formatName,
        string filePath,
        bool loop,
        CancellationToken cancellationToken)
    {
        var videoStream = OpenVideoStreamFromFile(formatName, filePath, CreateStreamParameters());
        await using (videoStream.ConfigureAwait(false))
        {
            await StreamFromCoreAsync(videoStream, loop, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task StreamFromContainerStreamCoreAsync(
        string formatName,
        Stream stream,
        bool loop,
        CancellationToken cancellationToken)
    {
        var videoStream = OpenVideoStreamFromStream(formatName, stream, CreateStreamParameters());
        await using (videoStream.ConfigureAwait(false))
        {
            await StreamFromCoreAsync(videoStream, loop, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task StreamFromCoreAsync(VideoStream videoStream, bool loop, CancellationToken cancellationToken)
    {
        var effectiveFrameRate = videoStream.FrameRate;
        if (effectiveFrameRate <= 0)
        {
            effectiveFrameRate = Settings.FrameRate;
        }

        if (effectiveFrameRate <= 0)
        {
            effectiveFrameRate = 30;
        }

        var frameInterval = TimeSpan.FromSeconds(1.0 / effectiveFrameRate);

        if (videoStream.HasFrame)
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteFrame(videoStream);

                if (!loop)
                {
                    break;
                }

                await Task.Delay(frameInterval, cancellationToken).ConfigureAwait(false);
            }
            while (true);

            return;
        }

        while (await videoStream.ReadNextFrameAsync(loop, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteFrame(videoStream);
            await Task.Delay(frameInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private VideoCodecParameters CreateStreamParameters()
        => new()
        {
            Width = Settings.Width,
            Height = Settings.Height,
            PixelFormat = Settings.PixelFormat,
            FrameRate = Settings.FrameRate,
        };

    private static VideoStream OpenVideoStreamFromFile(string formatName, string filePath, VideoCodecParameters parameters)
    {
        IDemuxer? demuxer = null;

        try
        {
            demuxer = CreateDemuxerOrThrow(formatName);

            if (demuxer.Open(filePath) != ContainerResult.Success)
            {
                throw new InvalidOperationException("Не удалось открыть файл '" + filePath + "'.");
            }

            var videoStream = VideoStream.OpenDemuxer(demuxer, parameters);
            demuxer = null;
            return videoStream;
        }
        finally
        {
            demuxer?.Dispose();
        }
    }

    private static VideoStream OpenVideoStreamFromStream(string formatName, Stream stream, VideoCodecParameters parameters)
    {
        IDemuxer? demuxer = null;

        try
        {
            demuxer = CreateDemuxerOrThrow(formatName);

            if (demuxer.Open(stream) != ContainerResult.Success)
            {
                throw new InvalidOperationException("Не удалось открыть поток.");
            }

            var videoStream = VideoStream.OpenDemuxer(demuxer, parameters);
            demuxer = null;
            return videoStream;
        }
        finally
        {
            demuxer?.Dispose();
        }
    }

    private static IDemuxer CreateDemuxerOrThrow(string formatName)
        => ContainerFactory.CreateDemuxer(formatName)
            ?? throw new NotSupportedException(
                "Демуксер для формата '" + formatName + "' не зарегистрирован.");

    /// <summary>
    /// Останавливает захват видеопотока.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public ValueTask StopCaptureAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        return backend.StopCaptureAsync(cancellationToken);
    }

    /// <summary>
    /// Останавливает захват видеопотока.
    /// </summary>
    public ValueTask StopCaptureAsync() => StopCaptureAsync(CancellationToken.None);

    /// <summary>
    /// Устанавливает значение контрола камеры (UVC-совместимый).
    /// </summary>
    /// <param name="control">Тип контрола.</param>
    /// <param name="value">Значение контрола.</param>
    public void SetControl(CameraControlType control, float value)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        backend.SetControl(control, value);
    }

    /// <summary>
    /// Получает текущее значение контрола камеры (UVC-совместимый).
    /// </summary>
    /// <param name="control">Тип контрола.</param>
    /// <returns>Текущее значение контрола.</returns>
    public float GetControl(CameraControlType control)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        return backend.GetControl(control);
    }

    /// <summary>
    /// Получает диапазон контрола камеры (min, max, default).
    /// </summary>
    /// <param name="control">Тип контрола.</param>
    /// <returns>Диапазон контрола или null, если неизвестен.</returns>
    public CameraControlRange? GetControlRange(CameraControlType control)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        return backend.GetControlRange(control);
    }

    /// <summary>
    /// Событие изменения контрола камеры внешним приложением.
    /// </summary>
    public event EventHandler<CameraControlChangedEventArgs>? ControlChanged
    {
        add => backend.ControlChanged += value;
        remove => backend.ControlChanged -= value;
    }

    /// <summary>
    /// Высвобождает ресурсы виртуальной камеры.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, value: true)) return;

        await backend.DisposeAsync().ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Создаёт новый экземпляр виртуальной камеры.
    /// </summary>
    /// <param name="settings">Настройки камеры.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Инициализированный экземпляр виртуальной камеры.</returns>
#pragma warning disable CA2000

    public static async ValueTask<VirtualCamera> CreateAsync(VirtualCameraSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateSettings(settings);

        var cameraBackend = CreateBackend();

        try
        {
            await cameraBackend.InitializeAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await cameraBackend.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new VirtualCamera(cameraBackend, settings);
    }

#pragma warning restore CA2000

    /// <summary>
    /// Создаёт новый экземпляр виртуальной камеры.
    /// </summary>
    /// <param name="settings">Настройки камеры.</param>
    /// <returns>Инициализированный экземпляр виртуальной камеры.</returns>
    public static ValueTask<VirtualCamera> CreateAsync(VirtualCameraSettings settings) => CreateAsync(settings, CancellationToken.None);

    private static void ValidateSettings(VirtualCameraSettings settings)
    {
        if (settings.Width < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Width, "Width должен быть больше 0.");
        }

        if (settings.Height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Height, "Height должен быть больше 0.");
        }

        if (settings.FrameRate < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.FrameRate, "FrameRate должен быть больше 0.");
        }

        if (settings.PixelFormat == VideoPixelFormat.Unknown)
        {
            throw new ArgumentException("Формат пикселей Unknown не поддерживается.", nameof(settings));
        }
    }

    private static IVirtualCameraBackend CreateBackend()
    {
        if (OperatingSystem.IsLinux()) return new LinuxCameraBackend();
        if (OperatingSystem.IsMacOS()) return new MacOSCameraBackend();
        if (OperatingSystem.IsWindows()) return new WindowsCameraBackend();

        throw new PlatformNotSupportedException(
            "Виртуальная камера не поддерживается на текущей платформе.");
    }

    private static IImageCodec CreateImageCodec(string extension)
    {
        return extension.ToUpperInvariant() switch
        {
            ".PNG" => new PngCodec(),
            ".WEBP" => new WebpCodec(),
            _ => throw new NotSupportedException(
                $"Формат изображения '{extension}' не поддерживается. Используйте PNG или WebP."),
        };
    }
}
