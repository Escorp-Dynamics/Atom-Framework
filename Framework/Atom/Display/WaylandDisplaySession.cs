using System.Diagnostics;
using System.Drawing;
using System.Runtime.Versioning;

using Atom.Display.Wayland;

using Microsoft.Extensions.Logging;

namespace Atom.Display;

/// <summary>
/// Сеанс дисплея на собственном композиторе Wayland.
/// </summary>
/// <remarks>
/// ★ Замена связки Xvfb/xpra. Отличия, ради которых всё делалось:
///
/// Ничего не нужно ставить. Прежний путь требовал внешних пакетов, а на сервере ещё и служб;
/// здесь весь дисплей — свой код и unix-сокет.
///
/// Ввод доверенный и без обходных путей. Раньше касания подавались через устройство ядра
/// (<c lang="text">/dev/uinput</c>), что требовало прав, udev-правил и совпадения матрицы устройства с
/// размером экрана. Теперь событие подаётся прямо в протокол, и для страницы оно неотличимо от
/// настоящего, включая <c lang="text">isTrusted</c> и <c lang="text">pointerType</c>.
///
/// Параллельность из коробки: у каждого сеанса свой сокет, борьбы за общий seat нет. Окно сразу
/// активно, поэтому браузер не режет частоту кадров фоновым вкладкам.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class WaylandDisplaySession : IAsyncDisposable
{
    private readonly WaylandCompositor compositor;
    private int isDisposed;

    private WaylandDisplaySession(WaylandCompositor compositor, WaylandDisplaySessionSettings settings)
    {
        this.compositor = compositor;
        Settings = settings;
    }

    /// <summary>Настройки сеанса.</summary>
    public WaylandDisplaySessionSettings Settings { get; init; }

    /// <summary>
    /// Смещения корневых окон последнего кадра каскадной раскладки, по порядку корней.
    /// </summary>
    /// <remarks>
    /// xdg-протокол не сообщает клиенту позицию окна; раскладку ведёт композитор. Потребитель —
    /// драйвер: экранные координаты кликов считаются с учётом каскадного сдвига окна.
    /// </remarks>
    /// <summary>
    /// Каскадное смещение окна с указанным индексом в сцене дисплея.
    /// </summary>
    /// <remarks>
    /// Chrome на Wayland не знает позицию своего окна; смещение ведёт композитор и сообщает его
    /// драйверу для пересчёта экранных координат кликов.
    /// </remarks>
    public (int X, int Y) GetWindowOffset(int windowIndex)
        => compositor.GetWindowOffset(windowIndex);

    /// <summary>Имя дисплея для переменной <c lang="text">WAYLAND_DISPLAY</c>.</summary>
    public string Display => compositor.DisplayName;

    /// <summary>Разрешение экрана.</summary>
    public Size Resolution => Settings.Resolution;

    /// <summary>Подача событий ввода.</summary>
    public WaylandInput Input => compositor.Input;

    /// <summary>
    /// Поднимает сеанс дисплея.
    /// </summary>
    public static WaylandDisplaySession Create(WaylandDisplaySessionSettings? settings = null)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Композитор Wayland работает только на Linux.");

        var effectiveSettings = settings ?? new WaylandDisplaySessionSettings();

        // Окно представляется тем же приложением, что будет на нём запущено, иначе прослойка видна на панели.
        var application = effectiveSettings.ApplicationPath is { Length: > 0 } path
            ? DesktopEntryResolver.Resolve(path)
            : (DesktopEntry?)null;

#pragma warning disable CA2000 // Владение композитором переходит сеансу; освобождает его DisposeAsync.
        var created = WaylandCompositor.Create(new WaylandCompositorSettings
        {
            Resolution = effectiveSettings.Resolution,
            HasTouch = effectiveSettings.HasTouch,
            EnablePresentation = effectiveSettings.IsVisible,
            OutputName = effectiveSettings.OutputName,
            WindowTitle = application?.Name,
            ApplicationId = application?.ApplicationId,
        });
#pragma warning restore CA2000

        effectiveSettings.Logger?.LogWaylandDisplayStarted(
            created.DisplayName,
            effectiveSettings.Resolution.Width,
            effectiveSettings.Resolution.Height,
            effectiveSettings.IsVisible);

        return new WaylandDisplaySession(created, effectiveSettings);
    }

    /// <summary>
    /// Готовит окружение процесса браузера под этот дисплей.
    /// </summary>
    /// <remarks>
    /// ★ <c lang="text">DISPLAY</c> снимается намеренно: при обеих заданных переменных браузер выбирает
    /// X11 и уходит мимо нашего композитора.
    /// </remarks>
    public void ConfigureEnvironment(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        startInfo.Environment["WAYLAND_DISPLAY"] = Display;
        startInfo.Environment["XDG_SESSION_TYPE"] = "wayland";
        startInfo.Environment.Remove("DISPLAY");

        // ★ Следы хозяйской сессии убираются: по ним приложение обращается к НАСТОЯЩЕЙ оболочке
        // мимо нашего композитора и заявляет о себе на её панели задач. Токен активации и
        // startup-id просят оболочку показать запуск, а имя рабочего стола включает интеграцию с
        // ней (порталы, глобальное меню, счётчики на значке).
        foreach (var inherited in HostSessionVariables)
            startInfo.Environment.Remove(inherited);

        if (Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime)
            startInfo.Environment["XDG_RUNTIME_DIR"] = runtime;

        var binary = Path.GetFileName(startInfo.FileName);

        // Приложения на GTK выбирают бэкенд по своим переменным, приложения на Chromium — по флагу.
        if (binary.Contains("firefox", StringComparison.OrdinalIgnoreCase)
            || binary.Contains("thunderbird", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.Environment["MOZ_ENABLE_WAYLAND"] = "1";
            startInfo.Environment["GDK_BACKEND"] = "wayland";
            return;
        }

        if (!IsChromiumBased(binary))
        {
            // Прочие приложения бэкенд выбирают сами; хватает WAYLAND_DISPLAY.
            startInfo.Environment["GDK_BACKEND"] = "wayland";
            startInfo.Environment["QT_QPA_PLATFORM"] = "wayland";
            return;
        }

        if (!startInfo.ArgumentList.Any(static argument => argument.StartsWith("--ozone-platform=", StringComparison.Ordinal)))
            startInfo.ArgumentList.Add("--ozone-platform=wayland");

        // Окно согласия при первом запуске перехватывает весь ввод на себя.
        AddIfMissing(startInfo, "--no-first-run");
        AddIfMissing(startInfo, "--no-default-browser-check");
    }

    private static bool IsChromiumBased(string binary)
    {
        foreach (var marker in ChromiumMarkers)
        {
            if (binary.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static readonly string[] ChromiumMarkers =
        ["chrome", "chromium", "edge", "brave", "opera", "vivaldi", "yandex", "electron"];

    /// <summary>
    /// Переменные запуска хозяйской сессии, по которым приложение заявляет о себе её оболочке.
    /// </summary>
    /// <remarks>
    /// ★ Список намеренно узкий — только разовые токены запуска. 'XDG_CURRENT_DESKTOP' и родственные
    /// сюда НЕ входят: по ним Chromium выбирает портал, тему и способ работы с оболочкой, и их
    /// снятие меняет поведение самого браузера, а не только видимость на панели задач.
    /// </remarks>
    private static readonly string[] HostSessionVariables =
    [
        "XDG_ACTIVATION_TOKEN",
        "DESKTOP_STARTUP_ID",
        "GIO_LAUNCHED_DESKTOP_FILE",
        "GIO_LAUNCHED_DESKTOP_FILE_PID",
    ];

    private static void AddIfMissing(ProcessStartInfo startInfo, string argument)
    {
        if (!startInfo.ArgumentList.Contains(argument, StringComparer.Ordinal))
            startInfo.ArgumentList.Add(argument);
    }

    /// <summary>Число подключённых к дисплею клиентов.</summary>
    public int ClientCount => compositor.ClientCount;

    /// <summary>Последний заголовок, заявленный приложением.</summary>
    public string? LastWindowTitle => compositor.LastWindowTitle;

    /// <summary>
    /// Нажимает клавишу и отпускает её.
    /// </summary>
    /// <param name="key">Клавиша.</param>
    /// <param name="modifiers">Удерживаемые модификаторы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask PressKeyAsync(ConsoleKey key, ConsoleModifiers modifiers = default, CancellationToken cancellationToken = default)
    {
        if (WaylandKeyCodes.FromConsoleKey(key) is not { } code)
            throw new NotSupportedException("Клавиша не имеет кода ядра: " + key);

        var held = new List<uint>(capacity: 3);

        if (modifiers.HasFlag(ConsoleModifiers.Shift))
            held.Add(WaylandKeyCodes.LeftShift);

        if (modifiers.HasFlag(ConsoleModifiers.Control))
            held.Add(WaylandKeyCodes.LeftControl);

        if (modifiers.HasFlag(ConsoleModifiers.Alt))
            held.Add(WaylandKeyCodes.LeftAlt);

        foreach (var modifier in held)
        {
            Input.SendKey(modifier, pressed: true);
            await Task.Delay(KeyStrokeDelay, cancellationToken).ConfigureAwait(false);
        }

        Input.SendKey(code, pressed: true);
        await Task.Delay(KeyStrokeDelay, cancellationToken).ConfigureAwait(false);
        Input.SendKey(code, pressed: false);

        // Модификаторы отпускаются в обратном порядке — так же, как это делает человек.
        for (var index = held.Count - 1; index >= 0; --index)
        {
            await Task.Delay(KeyStrokeDelay, cancellationToken).ConfigureAwait(false);
            Input.SendKey(held[index], pressed: false);
        }
    }

    private static readonly TimeSpan KeyStrokeDelay = TimeSpan.FromMilliseconds(18);

    /// <summary>
    /// Границы окна приложения внутри поверхности.
    /// </summary>
    /// <remarks>
    /// ★ Заменяет поиск окна через X11. Клиент рисует поверхность больше окна — по краям идёт
    /// тень, и координаты ввода считаются от поверхности, а не от окна.
    /// </remarks>
    public Rectangle WindowBounds => GetWindowBoundsByIndex(0);

    /// <summary>
    /// Границы окна по его ПОРЯДКОВОМУ индексу (порядок создания окон браузера): при N окнах
    /// каждое имеет свою позицию в сцене, и клики второго окна резолвятся в его геометрию.
    /// </summary>
    /// <param name="windowIndex">Индекс окна: 0 — первое созданное.</param>
    public Rectangle GetWindowBoundsByIndex(int windowIndex)
    {
        var (x, y, width, height) = compositor.GetWindowGeometryByIndex(windowIndex);
        return new Rectangle(x, y, width, height);
    }

    /// <summary>
    /// Находит номер окна композитора по заголовку.
    /// </summary>
    /// <remarks>
    /// Драйвер нумерует окна своим счётчиком; заголовок связывает эту нумерацию с настоящими
    /// окнами композитора и переживает появление служебных окон браузера.
    /// </remarks>
    /// <param name="windowTitle">Заголовок искомого окна.</param>
    /// <returns>Номер окна либо <see langword="null"/>, если заголовок не найден или неоднозначен.</returns>
    public int? TryResolveWindowIndexByTitle(string? windowTitle)
        => compositor.TryResolveWindowIndexByTitle(windowTitle);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) is not 0)
            return;

        await compositor.DisposeAsync().ConfigureAwait(false);
        Settings.Logger?.LogWaylandDisplayStopped(Display);
    }
}

/// <summary>
/// Настройки сеанса дисплея на композиторе Wayland.
/// </summary>
public sealed record WaylandDisplaySessionSettings
{
    /// <summary>Логгер диагностики.</summary>
    public ILogger? Logger { get; init; }

    /// <summary>Разрешение экрана.</summary>
    public Size Resolution { get; init; } = new(1920, 1080);

    /// <summary>
    /// Показывать содержимое окном на реальном экране.
    /// </summary>
    /// <remarks>
    /// Только для отладки: включает копирование кадров в сессию разработчика. В боевом режиме
    /// оставлять выключенным — тогда кадры клиента даже не отображаются в память.
    /// </remarks>
    public bool IsVisible { get; init; }

    /// <summary>
    /// Заявлять сенсорный ввод.
    /// </summary>
    /// <remarks>
    /// Наблюдаемо страницей через <c lang="text">navigator.maxTouchPoints</c>, поэтому должно
    /// соответствовать личности, под которую маскируется вкладка.
    /// </remarks>
    public bool HasTouch { get; init; }

    /// <summary>Имя выхода, видимое клиенту.</summary>
    public string OutputName { get; init; } = "Atom-1";

    /// <summary>
    /// Путь к приложению, которое будет запущено на этом дисплее.
    /// </summary>
    /// <remarks>
    /// По нему окно получает заголовок и иконку приложения на панели задач. Без него видно, что
    /// между приложением и системой есть прослойка.
    /// </remarks>
    public string? ApplicationPath { get; init; }
}
