using System.Runtime.Versioning;
using System.Security.Cryptography;
using Atom.Hardware.Display;
using Atom.Hardware.Input.Backends;

namespace Atom.Hardware.Input;

/// <summary>
/// Представляет устройство виртуальной клавиатуры.
/// Создаёт виртуальное устройство ввода на уровне ОС
/// и генерирует нативные события клавиатуры, неотличимые от физических.
/// </summary>
/// <remarks>
/// <para>
/// Создание клавиатуры выполняется через фабричный метод <see cref="CreateAsync(VirtualKeyboardSettings, CancellationToken)"/>.
/// После создания можно нажимать клавиши и вводить текст.
/// </para>
/// <para>
/// На Linux используется <c>/dev/uinput</c> — виртуальное устройство
/// на уровне ядра. Требуется доступ к <c>/dev/uinput</c>
/// (пользователь в группе <c>input</c> или udev-правило).
/// </para>
/// <example>
/// <code>
/// await using var keyboard = await VirtualKeyboard.CreateAsync(new VirtualKeyboardSettings());
///
/// keyboard.KeyPress(ConsoleKey.A);
///
/// await keyboard.KeyPressAsync(ConsoleKey.Enter);
///
/// await keyboard.KeyPressAsync(ConsoleKey.C, ConsoleModifiers.Control);
/// </code>
/// </example>
/// </remarks>
public sealed class VirtualKeyboard : IAsyncDisposable
{
    private readonly IVirtualKeyboardBackend backend;
    private bool isDisposed;

    /// <summary>
    /// Настройки клавиатуры.
    /// </summary>
    public VirtualKeyboardSettings Settings { get; }

    /// <summary>
    /// Идентификатор виртуального устройства клавиатуры в системе.
    /// </summary>
    public string DeviceIdentifier => backend.DeviceIdentifier;

    private VirtualKeyboard(IVirtualKeyboardBackend backend, VirtualKeyboardSettings settings)
    {
        this.backend = backend;
        Settings = settings;
    }

    /// <summary>
    /// Нажимает клавишу (без отпускания).
    /// </summary>
    /// <param name="key">Клавиша.</param>
    public void KeyDown(ConsoleKey key)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        backend.KeyDown(key);
    }

    /// <summary>
    /// Отпускает клавишу.
    /// </summary>
    /// <param name="key">Клавиша.</param>
    public void KeyUp(ConsoleKey key)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        backend.KeyUp(key);
    }

    /// <summary>
    /// Синхронное нажатие и отпускание клавиши.
    /// </summary>
    /// <param name="key">Клавиша.</param>
    public void KeyPress(ConsoleKey key)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        backend.KeyDown(key);
        backend.KeyUp(key);
    }

    /// <summary>
    /// Синхронное нажатие клавиши с модификаторами.
    /// </summary>
    /// <param name="key">Клавиша.</param>
    /// <param name="modifiers">Модификаторы.</param>
    public void KeyPress(ConsoleKey key, ConsoleModifiers modifiers)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        PressModifiers(modifiers, down: true);
        backend.KeyDown(key);
        backend.KeyUp(key);
        PressModifiers(modifiers, down: false);
    }

    /// <summary>
    /// Нажатие и отпускание клавиши с реалистичной задержкой.
    /// </summary>
    /// <param name="key">Клавиша.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask KeyPressAsync(ConsoleKey key, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        try
        {
            backend.KeyDown(key);

            await Task.Delay(RandomNumberGenerator.GetInt32(30, 100), cancellationToken).ConfigureAwait(false);

            backend.KeyUp(key);
        }
        catch (OperationCanceledException)
        {
            TryReleaseKey(key);
            throw;
        }
    }

    /// <summary>
    /// Нажатие клавиши с модификаторами (Ctrl, Shift, Alt) и реалистичными задержками.
    /// </summary>
    /// <param name="key">Клавиша.</param>
    /// <param name="modifiers">Модификаторы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask KeyPressAsync(ConsoleKey key, ConsoleModifiers modifiers, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        try
        {
            PressModifiers(modifiers, down: true);

            await Task.Delay(RandomNumberGenerator.GetInt32(10, 40), cancellationToken).ConfigureAwait(false);

            backend.KeyDown(key);

            await Task.Delay(RandomNumberGenerator.GetInt32(30, 100), cancellationToken).ConfigureAwait(false);

            backend.KeyUp(key);

            await Task.Delay(RandomNumberGenerator.GetInt32(5, 20), cancellationToken).ConfigureAwait(false);

            PressModifiers(modifiers, down: false);
        }
        catch (OperationCanceledException)
        {
            TryReleaseKey(key);
            TryReleaseModifiers(modifiers);
            throw;
        }
    }

    /// <summary>
    /// Высвобождает ресурсы виртуальной клавиатуры и уничтожает устройство.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, value: true)) return;

        await backend.DisposeAsync().ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Создаёт новый экземпляр виртуальной клавиатуры.
    /// </summary>
    /// <param name="settings">Настройки клавиатуры.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Инициализированный экземпляр виртуальной клавиатуры.</returns>
#pragma warning disable CA2000

    public static async ValueTask<VirtualKeyboard> CreateAsync(VirtualKeyboardSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var keyboardBackend = CreateBackend();

        try
        {
            await keyboardBackend.InitializeAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await keyboardBackend.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new VirtualKeyboard(keyboardBackend, settings);
    }

#pragma warning restore CA2000

    /// <summary>
    /// Создаёт новый экземпляр виртуальной клавиатуры с настройками по умолчанию.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Инициализированный экземпляр виртуальной клавиатуры.</returns>
    public static ValueTask<VirtualKeyboard> CreateDefaultAsync(CancellationToken cancellationToken = default)
        => CreateAsync(new VirtualKeyboardSettings(), cancellationToken);

    /// <summary>
    /// Создаёт виртуальную клавиатуру для указанного виртуального дисплея.
    /// Использует XTEST для инжекции событий напрямую в X-сервер.
    /// Клавиатура изолирована от физического ввода.
    /// </summary>
    /// <param name="display">Виртуальный дисплей.</param>
    /// <param name="settings">Настройки клавиатуры.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Инициализированный экземпляр виртуальной клавиатуры.</returns>
    [SupportedOSPlatform("linux")]
#pragma warning disable CA2000
    public static async ValueTask<VirtualKeyboard> CreateForDisplayAsync(
        VirtualDisplay display,
        VirtualKeyboardSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(display);
        display.ThrowIfUnavailable();

        var effectiveSettings = settings ?? new VirtualKeyboardSettings();

        return new VirtualKeyboard(
            await CreateXTestBackendAsync(display.Display, effectiveSettings, cancellationToken).ConfigureAwait(false),
            effectiveSettings);
    }

    /// <summary>
    /// Создаёт виртуальную клавиатуру для текущего X11-дисплея из переменной окружения DISPLAY.
    /// Использует XTEST для инжекции событий в системный X-сервер.
    /// </summary>
    /// <param name="settings">Настройки клавиатуры.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Инициализированный экземпляр виртуальной клавиатуры.</returns>
    [SupportedOSPlatform("linux")]
#pragma warning disable CA2000
    public static async ValueTask<VirtualKeyboard> CreateForCurrentDisplayAsync(
        VirtualKeyboardSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        var displayName = Environment.GetEnvironmentVariable("DISPLAY");
        if (string.IsNullOrWhiteSpace(displayName))
            throw new VirtualKeyboardException("DISPLAY не задан. Невозможно создать XTEST-клавиатуру для текущего системного дисплея.");

        var effectiveSettings = settings ?? new VirtualKeyboardSettings();

        return new VirtualKeyboard(
            await CreateXTestBackendAsync(displayName, effectiveSettings, cancellationToken).ConfigureAwait(false),
            effectiveSettings);
    }
#pragma warning restore CA2000

    [SupportedOSPlatform("linux")]
    private static async ValueTask<XTestKeyboardBackend> CreateXTestBackendAsync(
        string displayName,
        VirtualKeyboardSettings settings,
        CancellationToken cancellationToken)
    {
        var xtestBackend = new XTestKeyboardBackend();
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

    private void PressModifiers(ConsoleModifiers modifiers, bool down)
    {
        if (modifiers == default) return;

        if (down)
        {
            backend.ModifierDown(modifiers);
        }
        else
        {
            backend.ModifierUp(modifiers);
        }
    }

    private static IVirtualKeyboardBackend CreateBackend()
    {
        if (OperatingSystem.IsLinux()) return new LinuxKeyboardBackend();
        if (OperatingSystem.IsMacOS()) return new MacOSKeyboardBackend();
        if (OperatingSystem.IsWindows()) return new WindowsKeyboardBackend();

        throw new PlatformNotSupportedException(
            "Виртуальная клавиатура не поддерживается на текущей платформе.");
    }

    private void TryReleaseKey(ConsoleKey key)
    {
        try
        {
            backend.KeyUp(key);
        }
        catch (ObjectDisposedException)
        {
            // Best-effort cleanup during cancellation.
        }
    }

    private void TryReleaseModifiers(ConsoleModifiers modifiers)
    {
        try
        {
            PressModifiers(modifiers, down: false);
        }
        catch (ObjectDisposedException)
        {
            // Best-effort cleanup during cancellation.
        }
    }
}
