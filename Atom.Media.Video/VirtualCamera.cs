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
    private int cameraId = -1;

    private static readonly SemaphoreSlim locker = new(1, 1);
    private static readonly string[] packages = ["ffmpeg", "libv4l-dev", "v4l2loopback-dkms", "v4l2loopback-utils"];
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
    /// Путь к устройству камеры.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="VirtualCamera"/>.
    /// </summary>
    public VirtualCamera()
    {
        Path = GetPath();
        stream = new VideoStream(resolution, MediaFormat.YUYV, 30);
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
    protected virtual void OnNameChanged([NotNull] string name) => V4L2.SetInfo(cameraId, Name, version, vendor);

    /// <summary>
    /// Происходит в момент изменения версии камеры.
    /// </summary>
    /// <param name="version">Новая версия камеры.</param>
    protected virtual void OnVersionChanged([NotNull] Version version) => V4L2.SetInfo(cameraId, Name, version, vendor);

    /// <summary>
    /// Происходит в момент изменения поставщика камеры.
    /// </summary>
    /// <param name="vendor">Новый поставщик камеры.</param>
    protected virtual void OnVendorChanged([NotNull] string vendor) => V4L2.SetInfo(cameraId, Name, version, vendor);

    /// <summary>
    /// Начинает захват видеопотока с камеры.
    /// </summary>
    public unsafe virtual void StartCapture()
    {
        Wait.Until(() => !isPackagesInstalled || !File.Exists(Path));
        ObjectDisposedException.ThrowIf(isDisposed, this);

        cameraId = V4L2.Open(Path);

        V4L2.SetInfo(cameraId, Name, version, vendor);
        V4L2.SetFormat(cameraId, resolution, MediaFormat.YUYV);
        
        stream.Output = Path;
    }

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    public virtual async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;

        if (cameraId >= 0) V4L2.Close(cameraId);
        await OS.Terminal.RunAsync($"v4l2loopback-ctl remove {Path}").ConfigureAwait(false);

        await stream.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private static string GetPath()
    {
        for (var i = 0; i < int.MaxValue; ++i)
        {
            var path = $"/dev/video{i}";
            if (!File.Exists(path)) return path;
        }

        throw new VirtualCameraException("Превышен лимит допустимых камер");
    }

    /// <summary>
    /// Инициализирует устройства.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    public static async ValueTask InitAsync(CancellationToken cancellationToken)
    {
        await locker.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (!isPackagesInstalled)
        {
            foreach (var package in packages)
                if (!await OS.PM.CheckExistsAsync(package, cancellationToken).ConfigureAwait(false))
                    await OS.PM.InstallAsync(package, cancellationToken).ConfigureAwait(false);

            if (!await OS.Terminal.RunAsync($"modprobe -r v4l2loopback", cancellationToken).ConfigureAwait(false))
            {
                locker.Release();
                throw new VirtualCameraException("Не удалось очистить список устройств");
            }

            if (!await OS.Terminal.RunAsync($"modprobe v4l2loopback", cancellationToken).ConfigureAwait(false))
            {
                locker.Release();
                throw new VirtualCameraException("Не удалось инициализировать устройства");
            }

            isPackagesInstalled = true;
        }

        locker.Release();
    }

    /// <summary>
    /// Инициализирует устройства.
    /// </summary>
    public static ValueTask InitAsync() => InitAsync(CancellationToken.None);
}