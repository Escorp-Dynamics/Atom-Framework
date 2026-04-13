using System.Drawing;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Atom.Hardware.Display;
using Atom.Hardware.Input.Backends;

namespace Atom.Hardware.Input;

/// <summary>
/// Представляет устройство виртуальной мыши.
/// Создаёт виртуальное устройство ввода на уровне ОС
/// и генерирует нативные события мыши, неотличимые от физических.
/// </summary>
/// <remarks>
/// <para>
/// Создание мыши выполняется через фабричный метод <see cref="CreateAsync(VirtualMouseSettings, CancellationToken)"/>.
/// После создания можно перемещать курсор, кликать и скроллить.
/// </para>
/// <para>
/// На Linux используется <c>/dev/uinput</c> — виртуальное устройство
/// на уровне ядра. Требуется доступ к <c>/dev/uinput</c>
/// (пользователь в группе <c>input</c> или udev-правило).
/// </para>
/// <example>
/// <code>
/// var settings = new VirtualMouseSettings { ScreenSize = new(1920, 1080) };
/// await using var mouse = await VirtualMouse.CreateAsync(settings);
///
/// mouse.MoveAbsolute(new(500, 300));
/// mouse.Click();
///
/// await mouse.ClickAtAsync(new(800, 600));
/// </code>
/// </example>
/// </remarks>
public sealed class VirtualMouse : IAsyncDisposable
{
    private readonly IVirtualMouseBackend backend;
    private bool isDisposed;

    /// <summary>
    /// Настройки мыши.
    /// </summary>
    public VirtualMouseSettings Settings { get; }

    /// <summary>
    /// Идентификатор виртуального устройства мыши в системе.
    /// </summary>
    public string DeviceIdentifier => backend.DeviceIdentifier;

    /// <summary>
    /// Указывает, имеет ли виртуальная мышь отдельный курсор,
    /// независимый от курсора реальной мыши (Multi-Pointer X).
    /// </summary>
    public bool HasSeparateCursor => backend.HasSeparateCursor;

    private VirtualMouse(IVirtualMouseBackend backend, VirtualMouseSettings settings)
    {
        this.backend = backend;
        Settings = settings;
    }

    /// <summary>
    /// Перемещает курсор в абсолютные координаты экрана.
    /// </summary>
    /// <param name="position">Позиция на экране.</param>
    public void MoveAbsolute(Point position)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        backend.MoveAbsolute(position);
    }

    /// <summary>
    /// Перемещает курсор относительно текущей позиции.
    /// </summary>
    /// <param name="delta">Смещение.</param>
    public void MoveRelative(Size delta)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        backend.MoveRelative(delta);
    }

    /// <summary>
    /// Нажимает кнопку мыши (без отпускания).
    /// </summary>
    /// <param name="button">Кнопка.</param>
    public void ButtonDown(VirtualMouseButton button = VirtualMouseButton.Left)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        backend.ButtonDown(button);
    }

    /// <summary>
    /// Отпускает кнопку мыши.
    /// </summary>
    /// <param name="button">Кнопка.</param>
    public void ButtonUp(VirtualMouseButton button = VirtualMouseButton.Left)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        backend.ButtonUp(button);
    }

    /// <summary>
    /// Синхронный клик: нажатие и отпускание кнопки мыши.
    /// </summary>
    /// <param name="button">Кнопка.</param>
    public void Click(VirtualMouseButton button = VirtualMouseButton.Left)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        backend.ButtonDown(button);
        backend.ButtonUp(button);
    }

    /// <summary>
    /// Клик с реалистичной задержкой между нажатием и отпусканием.
    /// </summary>
    /// <param name="button">Кнопка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask ClickAsync(VirtualMouseButton button = VirtualMouseButton.Left, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        try
        {
            backend.ButtonDown(button);

            await Task.Delay(RandomNumberGenerator.GetInt32(40, 120), cancellationToken).ConfigureAwait(false);

            backend.ButtonUp(button);
        }
        catch (OperationCanceledException)
        {
            TryReleaseButton(button);
            throw;
        }
    }

    /// <summary>
    /// Перемещает курсор в указанные координаты и кликает (синхронно).
    /// </summary>
    /// <param name="position">Позиция на экране.</param>
    /// <param name="button">Кнопка.</param>
    public void ClickAt(Point position, VirtualMouseButton button = VirtualMouseButton.Left)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        backend.MoveAbsolute(position);
        backend.ButtonDown(button);
        backend.ButtonUp(button);
    }

    /// <summary>
    /// Перемещает курсор в указанные координаты и кликает с реалистичными задержками.
    /// </summary>
    /// <param name="position">Позиция на экране.</param>
    /// <param name="button">Кнопка.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask ClickAtAsync(Point position, VirtualMouseButton button = VirtualMouseButton.Left, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        try
        {
            backend.MoveAbsolute(position);
            await Task.Delay(RandomNumberGenerator.GetInt32(8, 30), cancellationToken).ConfigureAwait(false);

            backend.ButtonDown(button);

            await Task.Delay(RandomNumberGenerator.GetInt32(40, 120), cancellationToken).ConfigureAwait(false);

            backend.ButtonUp(button);
        }
        catch (OperationCanceledException)
        {
            TryReleaseButton(button);
            throw;
        }
    }

    /// <summary>
    /// Прокрутка вертикального колеса.
    /// </summary>
    /// <param name="delta">Дельта прокрутки (положительное — вверх).</param>
    public void Scroll(int delta)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        backend.Scroll(delta);
    }

    /// <summary>
    /// Прокрутка горизонтального колеса.
    /// </summary>
    /// <param name="delta">Дельта прокрутки (положительное — вправо).</param>
    public void ScrollHorizontal(int delta)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        backend.ScrollHorizontal(delta);
    }

    /// <summary>
    /// Высвобождает ресурсы виртуальной мыши и уничтожает устройство.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, value: true)) return;

        await backend.DisposeAsync().ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Создаёт новый экземпляр виртуальной мыши.
    /// </summary>
    /// <param name="settings">Настройки мыши.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Инициализированный экземпляр виртуальной мыши.</returns>
#pragma warning disable CA2000

    public static async ValueTask<VirtualMouse> CreateAsync(VirtualMouseSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateSettings(settings);

        var mouseBackend = CreateBackend();

        try
        {
            await mouseBackend.InitializeAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await mouseBackend.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new VirtualMouse(mouseBackend, settings);
    }

#pragma warning restore CA2000

    /// <summary>
    /// Создаёт новый экземпляр виртуальной мыши с настройками по умолчанию.
    /// На Linux автоматически определяет размер виртуального экрана через X11.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Инициализированный экземпляр виртуальной мыши.</returns>
    public static ValueTask<VirtualMouse> CreateDefaultAsync(CancellationToken cancellationToken = default)
        => CreateDefaultAsync(new VirtualMouseSettings(), cancellationToken);

    /// <summary>
    /// Создаёт новый экземпляр виртуальной мыши на основе переданных настроек по умолчанию.
    /// На Linux автоматически определяет размер виртуального экрана через X11,
    /// сохраняя остальные параметры вызывающей стороны.
    /// </summary>
    /// <param name="settings">Базовые настройки мыши.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Инициализированный экземпляр виртуальной мыши.</returns>
    public static ValueTask<VirtualMouse> CreateDefaultAsync(VirtualMouseSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var effectiveSettings = settings;

        if (OperatingSystem.IsLinux())
        {
            var detected = LinuxMouseBackend.DetectVirtualScreenSize();
            if (detected is { } size)
                effectiveSettings = effectiveSettings with { ScreenSize = size };
        }

        return CreateAsync(effectiveSettings, cancellationToken);
    }

    /// <summary>
    /// Создаёт виртуальную мышь для указанного виртуального дисплея.
    /// Использует XTEST для инжекции событий напрямую в X-сервер.
    /// События мыши изолированы в пределах указанного X-сервера.
    /// </summary>
    /// <param name="display">Виртуальный дисплей.</param>
    /// <param name="settings">Настройки мыши.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Инициализированный экземпляр виртуальной мыши.</returns>
    [SupportedOSPlatform("linux")]
#pragma warning disable CA2000
    public static async ValueTask<VirtualMouse> CreateForDisplayAsync(
        VirtualDisplay display,
        VirtualMouseSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(display);
        display.ThrowIfUnavailable();

        var effectiveSettings = (settings ?? new VirtualMouseSettings()) with
        {
            ScreenSize = display.Resolution,
            UseSeparateCursor = false,
        };

        ValidateSettings(effectiveSettings);

        return new VirtualMouse(
            await CreateXTestBackendAsync(display.Display, effectiveSettings, cancellationToken).ConfigureAwait(false),
            effectiveSettings);
    }

    /// <summary>
    /// Создаёт виртуальную мышь для текущего X11-дисплея из переменной окружения DISPLAY.
    /// Использует XTEST и общий системный курсор без создания отдельного MPX-устройства.
    /// </summary>
    /// <param name="settings">Настройки мыши.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Инициализированный экземпляр виртуальной мыши.</returns>
    [SupportedOSPlatform("linux")]
#pragma warning disable CA2000
    public static async ValueTask<VirtualMouse> CreateForCurrentDisplayAsync(
        VirtualMouseSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        var displayName = Environment.GetEnvironmentVariable("DISPLAY");
        if (string.IsNullOrWhiteSpace(displayName))
            throw new VirtualMouseException("DISPLAY не задан. Невозможно создать XTEST-мышь для текущего системного дисплея.");

        var effectiveSettings = settings ?? new VirtualMouseSettings();

        if (OperatingSystem.IsLinux())
        {
            var detected = LinuxMouseBackend.DetectVirtualScreenSize();
            effectiveSettings = detected is { } size
                ? effectiveSettings with { ScreenSize = size, UseSeparateCursor = false }
                : effectiveSettings with { UseSeparateCursor = false };
        }

        ValidateSettings(effectiveSettings);

        return new VirtualMouse(
            await CreateXTestBackendAsync(displayName, effectiveSettings, cancellationToken).ConfigureAwait(false),
            effectiveSettings);
    }
#pragma warning restore CA2000

    [SupportedOSPlatform("linux")]
    private static async ValueTask<XTestMouseBackend> CreateXTestBackendAsync(
        string displayName,
        VirtualMouseSettings settings,
        CancellationToken cancellationToken)
    {
        var xtestBackend = new XTestMouseBackend();
        xtestBackend.SetDisplayName(displayName);

        try
        {
            await xtestBackend.InitializeAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await xtestBackend.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return xtestBackend;
    }

    private static void ValidateSettings(VirtualMouseSettings settings)
    {
        if (settings.ScreenSize.Width < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.ScreenSize.Width, "ScreenSize.Width должен быть больше 0.");
        }

        if (settings.ScreenSize.Height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.ScreenSize.Height, "ScreenSize.Height должен быть больше 0.");
        }
    }

    private static IVirtualMouseBackend CreateBackend()
    {
        if (OperatingSystem.IsLinux()) return new LinuxMouseBackend();
        if (OperatingSystem.IsMacOS()) return new MacOSMouseBackend();
        if (OperatingSystem.IsWindows()) return new WindowsMouseBackend();

        throw new PlatformNotSupportedException(
            "Виртуальная мышь не поддерживается на текущей платформе.");
    }

    private void TryReleaseButton(VirtualMouseButton button)
    {
        try
        {
            backend.ButtonUp(button);
        }
        catch (ObjectDisposedException)
        {
            // Best-effort cleanup during cancellation.
        }
    }
}
