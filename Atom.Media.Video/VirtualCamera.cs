#pragma warning disable IDE0060

using Atom.Architect.Reactive;
using Atom.Distribution;
using Atom.Threading;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;

namespace Atom.Media.Video;

/// <summary>
/// Представляет устройство виртуальной камеры.
/// </summary>
public partial class VirtualCamera : Reactively, IAsyncDisposable
{
    private readonly VideoStream stream;

    private bool isDisposed;

    /// <summary>
    /// Разрешение камеры.
    /// </summary>
    [Reactively]
    private Size resolution = new(640, 480);

    /// <summary>
    /// Частота кадров.
    /// </summary>
    [Reactively]
    private int frameRate = 30;

    /// <summary>
    /// Название камеры.
    /// </summary>
    [Reactively]
    private string name = "Virtual Camera";

    /// <summary>
    /// Версия камеры.
    /// </summary>
    [Reactively]
    private Version version = new(1, 0);

    /// <summary>
    /// Производитель.
    /// </summary>
    [Reactively]
    private string vendor = "Escorp Labs";

    /// <summary>
    /// Определяет, будет ли камера с выключенным звуком.
    /// </summary>
    [Reactively]
    private bool isMuted;

    /// <summary>
    /// Номер камеры в системе.
    /// </summary>
    public int Number { get; }

    /// <summary>
    /// Путь к устройству камеры.
    /// </summary>
    public string Path => $"/dev/video{Number}";

    /// <summary>
    /// Определяет, активен ли захват видеопотока.
    /// </summary>
    public bool IsCapturing { get; protected set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VirtualCamera"/>.
    /// </summary>
    public VirtualCamera()
    {
        Number = GetCameraNumber();
        stream = new VideoStream(resolution);
        ResolutionChanged += (sender, args) => OnResolutionChanged(resolution);
        NameChanged += (sender, args) => OnNameChanged(name);
        VersionChanged += (sender, args) => OnVersionChanged(version);
        VendorChanged += (sender, args) => OnVendorChanged(vendor);
        IsMutedChanged += (sender, args) => OnIsMutedChanged(isMuted);
    }

    /// <summary>
    /// Происходит в момент изменения разрешения камеры.
    /// </summary>
    /// <param name="resolution">Новое разрешение камеры.</param>
    protected virtual void OnResolutionChanged(Size resolution) => stream.Resolution = resolution;

    /// <summary>
    /// Происходит в момент изменения названия камеры.
    /// </summary>
    /// <param name="name">Новое название камеры.</param>
    protected virtual void OnNameChanged([NotNull] string name) { }

    /// <summary>
    /// Происходит в момент изменения версии камеры.
    /// </summary>
    /// <param name="version">Новая версия камеры.</param>
    protected virtual void OnVersionChanged([NotNull] Version version) { }

    /// <summary>
    /// Происходит в момент изменения поставщика камеры.
    /// </summary>
    /// <param name="vendor">Новый поставщик камеры.</param>
    protected virtual void OnVendorChanged([NotNull] string vendor) { }

    /// <summary>
    /// Происходит в момент изменения параметра использования звука.
    /// </summary>
    /// <param name="isMuted">Указывает, используется ли звук.</param>
    protected virtual void OnIsMutedChanged(bool isMuted) => stream.IsMuted = isMuted;

    /// <summary>
    /// Начинает захват видеопотока с камеры.
    /// </summary>
    /// <param name="path">Путь к файлу видеозахвата.</param>
    /// <param name="isLooped">Указывает, является ли захват зацикленным.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public virtual async ValueTask StartCaptureAsync(string path, bool isLooped, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (!string.IsNullOrEmpty(path)) stream.Input = path;
        if (IsCapturing) return;

        IsCapturing = true;
        stream.IsLooped = isLooped;

        if (!await OS.Terminal.RunAsAdministratorAsync($"modprobe v4l2loopback devices=1 video_nr={Number} card_label='{name} {Number}' exclusive_caps=1 width={resolution.Width} height={resolution.Height} framerate={frameRate}/1 pixel_format=MJPG", cancellationToken).ConfigureAwait(false))
            throw new VirtualCameraException("Не удалось запустить камеру");

        //if (!await OS.Terminal.RunAsAdministratorAsync($"v4l2loopback-ctl set-caps {Path} YUV420P:{resolution.Width}x{resolution.Height}@{frameRate}/1", cancellationToken).ConfigureAwait(false))
        //    throw new VirtualCameraException("Не удалось установить fps");

        if (!await OS.Terminal.RunAsAdministratorAsync($"chmod 777 {Path}", cancellationToken).ConfigureAwait(false))
            throw new VirtualCameraException("Не удалось установить права доступа к камере");

        await Wait.UntilAsync(() => !File.Exists(Path), cancellationToken).ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(isDisposed, this);

        stream.Output = Path;
    }

    /// <summary>
    /// Начинает захват видеопотока с камеры.
    /// </summary>
    /// <param name="path">Путь к файлу видеозахвата.</param>
    /// <param name="isLooped">Указывает, является ли захват зацикленным.</param>
    public ValueTask StartCaptureAsync(string path, bool isLooped) => StartCaptureAsync(path, isLooped, CancellationToken.None);

    /// <summary>
    /// Начинает захват видеопотока с камеры.
    /// </summary>
    /// <param name="path">Путь к файлу видеозахвата.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public ValueTask StartCaptureAsync(string path, CancellationToken cancellationToken) => StartCaptureAsync(path, default, cancellationToken);

    /// <summary>
    /// Начинает захват видеопотока с камеры.
    /// </summary>
    /// <param name="path">Путь к файлу видеозахвата.</param>
    public ValueTask StartCaptureAsync(string path) => StartCaptureAsync(path, default, CancellationToken.None);

    /// <summary>
    /// Начинает захват видеопотока с камеры.
    /// </summary>
    /// <param name="url">Ссылка к файлу видеозахвата.</param>
    /// <param name="isLooped">Указывает, является ли захват зацикленным.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public ValueTask StartCaptureAsync([NotNull] Uri url, bool isLooped, CancellationToken cancellationToken) => StartCaptureAsync(url.AbsoluteUri, isLooped, cancellationToken);

    /// <summary>
    /// Начинает захват видеопотока с камеры.
    /// </summary>
    /// <param name="url">Ссылка к файлу видеозахвата.</param>
    /// <param name="isLooped">Указывает, является ли захват зацикленным.</param>
    public ValueTask StartCaptureAsync(Uri url, bool isLooped) => StartCaptureAsync(url, isLooped, CancellationToken.None);

    /// <summary>
    /// Начинает захват видеопотока с камеры.
    /// </summary>
    /// <param name="url">Ссылка к файлу видеозахвата.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public ValueTask StartCaptureAsync([NotNull] Uri url, CancellationToken cancellationToken) => StartCaptureAsync(url, default, cancellationToken);

    /// <summary>
    /// Начинает захват видеопотока с камеры.
    /// </summary>
    /// <param name="url">Ссылка к файлу видеозахвата.</param>
    public ValueTask StartCaptureAsync([NotNull] Uri url) => StartCaptureAsync(url, default, CancellationToken.None);

    /// <summary>
    /// Начинает захват видеопотока с камеры.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public ValueTask StartCaptureAsync(CancellationToken cancellationToken) => StartCaptureAsync(string.Empty, cancellationToken);

    /// <summary>
    /// Начинает захват видеопотока с камеры.
    /// </summary>
    public ValueTask StartCaptureAsync() => StartCaptureAsync(CancellationToken.None);

    /// <summary>
    /// Останавливает захват видеопотока.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public ValueTask StopCaptureAsync(CancellationToken cancellationToken)
    {
        IsCapturing = default;
        stream.Input = stream.Output = string.Empty;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Останавливает захват видеопотока.
    /// </summary>
    public ValueTask StopCaptureAsync() => StopCaptureAsync(CancellationToken.None);

    /// <summary>
    /// Начинает захват видеопотока и ожидает завершения.
    /// </summary>
    /// <param name="path">Путь к файлу видеозахвата.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public async ValueTask WaitForCaptureAsync(string path, CancellationToken cancellationToken)
    {
        await StartCaptureAsync(path, cancellationToken).ConfigureAwait(false);
        await WaitForCaptureAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Начинает захват видеопотока и ожидает завершения.
    /// </summary>
    /// <param name="path">Путь к файлу видеозахвата.</param>
    public ValueTask WaitForCaptureAsync(string path) => WaitForCaptureAsync(path, CancellationToken.None);

    /// <summary>
    /// Начинает захват видеопотока и ожидает завершения.
    /// </summary>
    /// <param name="url">Ссылка к файлу видеозахвата.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public async ValueTask WaitForCaptureAsync(Uri url, CancellationToken cancellationToken)
    {
        await StartCaptureAsync(url, cancellationToken).ConfigureAwait(false);
        await WaitForCaptureAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Начинает захват видеопотока и ожидает завершения.
    /// </summary>
    /// <param name="url">Ссылка к файлу видеозахвата.</param>
    public ValueTask WaitForCaptureAsync(Uri url) => WaitForCaptureAsync(url, CancellationToken.None);

    /// <summary>
    /// Ожидает завершения захвата видеопотока.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public ValueTask WaitForCaptureAsync(CancellationToken cancellationToken) => stream.WaitForEndingAsync(cancellationToken);

    /// <summary>
    /// Ожидает завершения захвата видеопотока.
    /// </summary>
    public ValueTask WaitForCaptureAsync() => WaitForCaptureAsync(CancellationToken.None);

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    public virtual async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;

        await OS.Terminal.RunAsAdministratorAsync($"v4l2loopback-ctl remove {Path}").ConfigureAwait(false);

        await stream.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private static int GetCameraNumber()
    {
        for (var i = 0; i < int.MaxValue; ++i)
        {
            var path = $"/dev/video{i}";
            if (!File.Exists(path)) return i;
        }

        throw new VirtualCameraException("Превышен лимит допустимых камер");
    }
}