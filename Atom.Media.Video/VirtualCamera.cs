using Atom.Architect.Reactive;
using Atom.Distribution;
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
    private int cameraFd = -1;

    private static readonly SemaphoreSlim locker = new(1, 1);
    private static readonly string[] packages = ["libv4l-dev", "v4l2loopback-dkms"];
    private static bool isPackagesInstalled;

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
    /// Инициализирует новый экземпляр <see cref="VirtualCamera"/>.
    /// </summary>
    public VirtualCamera()
    {
        stream = new VideoStream(resolution, PixelFormat.YUV420P);
        ResolutionChanged += (sender, args) => OnResolutionChanged(resolution);
        NameChanged += (sender, args) => OnNameChanged(name);
        VersionChanged += (sender, args) => OnVersionChanged(version);
        VendorChanged += (sender, args) => OnVendorChanged(vendor);
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
    protected virtual void OnNameChanged([NotNull] string name) => SetControl(V4L2.CID_NAME, name);

    /// <summary>
    /// Происходит в момент изменения версии камеры.
    /// </summary>
    /// <param name="version">Новая версия камеры.</param>
    protected virtual void OnVersionChanged([NotNull] Version version) => SetControl(V4L2.CID_VERSION, version.ToString());

    /// <summary>
    /// Происходит в момент изменения поставщика камеры.
    /// </summary>
    /// <param name="vendor">Новый поставщик камеры.</param>
    protected virtual void OnVendorChanged([NotNull] string vendor) => SetControl(V4L2.CID_MANUFACTURER, vendor);

    private void SetControl(int id, string value)
    {
        if (cameraFd < 0) return;

        var control = new V4L2.Control
        {
            id = (uint)id,
            value = value.GetHashCode(),
        };

        var result = V4L2.Ioctl(cameraFd, V4L2.VIDIOC_S_CTRL, ref control);
        if (result < 0) throw new VirtualCameraException($"Не удалось установить параметр {id}");
    }

    /// <summary>
    /// Начинает захват видеопотока с камеры.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public virtual async ValueTask StartCaptureAsync(CancellationToken cancellationToken)
    {
        await locker.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (!isPackagesInstalled)
        {
            foreach (var package in packages)
                if (!await OS.PM.CheckExistsAsync(package, cancellationToken).ConfigureAwait(false))
                    await OS.PM.InstallAsync(package, cancellationToken).ConfigureAwait(false);

            isPackagesInstalled = true;
        }

        var cameraPath = GetPath(out var cameraNumber);
        name += ' ' + cameraNumber;

        if (!await OS.Terminal.RunAsync($"modprobe v4l2loopback devices=1 video_nr={cameraNumber} card_label='{name}'", cancellationToken).ConfigureAwait(false))
            throw new VirtualCameraException("Не удалось инициализировать устройство");

        cameraFd = V4L2.Open(cameraPath, 0);

        if (cameraFd < 0)
        {
            locker.Release();
            throw new VirtualCameraException("Не удалось открыть устройство");
        }

        locker.Release();

        var format = new V4L2.Format
        {
            type = V4L2.BUF_TYPE_VIDEO_CAPTURE,
            fmt = new V4L2.PixelFormat
            {
                width = (uint)resolution.Width,
                height = (uint)resolution.Height,
                pixelFormat = V4L2.PIX_FMT_YUV420,
                field = V4L2.FIELD_NONE
            }
        };

        var result = V4L2.Ioctl(cameraFd, V4L2.VIDIOC_S_FMT, ref format);
        if (result < 0) throw new VirtualCameraException("Не удалось установить формат видео");

        SetControl(V4L2.CID_NAME, name);
        SetControl(V4L2.CID_VERSION, version.ToString());
        SetControl(V4L2.CID_MANUFACTURER, vendor);
    }

    /// <summary>
    /// Начинает захват видеопотока с камеры.
    /// </summary>
    public ValueTask StartCaptureAsync() => StartCaptureAsync(CancellationToken.None);

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    public virtual async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;

        if (cameraFd >= 0) _ = V4L2.Close(cameraFd);

        await stream.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private static string GetPath(out int number)
    {
        for (var i = 0; i < short.MaxValue; ++i)
        {
            var path = $"/dev/video{i}";

            if (!File.Exists(path))
            {
                number = i + 1;
                return path;
            }
        }

        throw new InvalidOperationException("Все камеры в системе уже заняты");
    }
}