using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.Versioning;

using Atom.Display.Wayland.Objects;
using Atom.Display.Wayland.Presentation;
using Atom.Display.Wayland.Protocol;

namespace Atom.Display.Wayland;

/// <summary>
/// Композитор Wayland для безголовой работы браузера.
/// </summary>
/// <remarks>
/// ★ Зачем свой. Браузеру нужна оконная система: без неё он не берёт ввод и не рисует. Готовые
/// решения тянут пакеты (композитор, libinput, seatd) и требуют прав root при установке, а нам
/// нужна работа из коробки на любом Linux.
///
/// Отсюда объём: реализуется ровно то, что браузер спрашивает — каталог интерфейсов, поверхности,
/// буферы разделяемой памяти, окна и ввод. Вывод изображения не нужен вовсе: буферы принимаются и
/// отбрасываются, поэтому DRM, GBM и EGL не задействованы.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class WaylandCompositor : IAsyncDisposable
{
    private readonly Socket listener;
    private readonly List<WaylandClient> clients = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<WaylandGlobal> globals = [];
    private uint nextSerial = 1;
    private uint nextGlobalName = 1;
    private Task? eventLoop;


    /// <summary>Первое хост-окно: используется диагностикой, курсором и захватом кадров.</summary>
    private WaylandPresentationWindow? PrimaryPresentation
        => presentations.TryGetValue(PrimaryWindowIndex, out var window) ? window : null;

    private const int PrimaryWindowIndex = 0;

    /// <summary>Хост-окна по числу окон браузера: каждое — запись в таскбаре системы.</summary>
    // ★ Ключ — НОМЕР окна браузера, а не позиция: номера монотонны и не переиспользуются, поэтому
    // список после закрытия хвостовых окон заставлял открывать окна под все пропущенные номера.
    private readonly Dictionary<int, WaylandPresentationWindow> presentations = [];

    // Снимок окон на время опроса: Pump может открыть новое окно, а менять коллекцию в обходе нельзя.
    private readonly List<WaylandPresentationWindow> pumpBuffer = [];
    private int presentedFrames;
    private int committedFrames;
    private string? lastPresentIssue;
    private int isDisposed;

    private WaylandCompositor(Socket listener, string socketPath, WaylandCompositorSettings settings)
    {
        this.listener = listener;
        SocketPath = socketPath;
        Settings = settings;
    }

    /// <summary>Путь к unix-сокету композитора.</summary>
    public string SocketPath { get; }

    /// <summary>Имя дисплея для переменной <c lang="text">WAYLAND_DISPLAY</c>.</summary>
    public string DisplayName => Path.GetFileName(SocketPath);

    /// <summary>Параметры композитора.</summary>
    public WaylandCompositorSettings Settings { get; }

    /// <summary>
    /// Каскадное смещение окна по его индексу: Chrome считает себя в (0,0), реальная позиция — здесь.
    /// </summary>
    /// <remarks>
    /// Шаг — четверть разрешения дисплея: окна укладываются «лесенкой», каждое видно целиком,
    /// даже когда размеры окон Chrome отличаются от разрешения дисплея.
    /// </remarks>
    public (int X, int Y) GetWindowOffset(int windowIndex)
    {
        var stepX = Settings.Resolution.Width / 4;
        var stepY = Settings.Resolution.Height / 4;
        return (windowIndex * stepX, windowIndex * stepY);
    }

    /// <summary>Выдаёт порядковый номер новому окну браузера.</summary>
    internal int AllocateWindowIndex() => windowIndexCounter++;

    private int windowIndexCounter;

    /// <summary>
    /// Находит номер окна по его заголовку.
    /// </summary>
    /// <remarks>
    /// ★ Драйвер нумерует свои окна сам и о номерах композитора не знает. Заголовок — единственное,
    /// что видят обе стороны: драйвер задаёт его странице, клиент передаёт <c lang="text">set_title</c>.
    /// Пока заголовок не совпал, номер драйвера остаётся разумной догадкой — он расходится лишь
    /// тогда, когда браузер заводит служебное окно.
    /// </remarks>
    /// <param name="windowTitle">Заголовок искомого окна.</param>
    /// <returns>Номер окна либо <see langword="null"/>, если заголовок не найден или неоднозначен.</returns>
    public int? TryResolveWindowIndexByTitle(string? windowTitle)
    {
        if (string.IsNullOrEmpty(windowTitle))
            return null;

        int? resolved = null;

        Invoke(() =>
        {
            foreach (var client in clients)
            {
                foreach (var toplevel in client.OfType<XdgToplevel>())
                {
                    if (!string.Equals(toplevel.Title, windowTitle, StringComparison.Ordinal))
                        continue;

                    // Два окна с одним заголовком различить нечем — пусть решает счётчик драйвера.
                    if (resolved is not null)
                    {
                        resolved = null;
                        return;
                    }

                    resolved = toplevel.WindowIndex;
                }
            }
        });

        return resolved;
    }

    /// <summary>Объявленные клиенту интерфейсы.</summary>
    internal IReadOnlyList<WaylandGlobal> Globals => globals;

    /// <summary>Подключённые клиенты.</summary>
    internal IReadOnlyList<WaylandClient> Clients => clients;

    /// <summary>Число подключённых клиентов.</summary>
    public int ClientCount => clients.Count;

    /// <summary>
    /// Сколько кадров показано окном вывода.
    /// </summary>
    /// <remarks>
    /// Годится для проверки, что картинка действительно идёт: при выключенном выводе всегда ноль.
    /// </remarks>
    public int PresentedFrameCount => presentedFrames;

    /// <summary>Сколько хост-окон открыто: каждое стоит буфера кадра и связи с оболочкой.</summary>
    public int PresentationCount
    {
        get
        {
            var count = 0;
            Invoke(() => count = presentations.Count);
            return count;
        }
    }

    /// <summary>Окно вывода живо и связь с сессией разработчика цела.</summary>
    /// <remarks>Читается из чужого потока, а коллекцию окон меняет цикл событий — отсюда Invoke.</remarks>
    public bool IsPresentationAlive
    {
        get
        {
            var isAlive = false;
            Invoke(() => isAlive = PrimaryPresentation?.IsAlive ?? false);
            return isAlive;
        }
    }

    /// <summary>Состояние окна вывода для диагностики.</summary>
    public string DescribePresentation()
    {
        var description = "";

        Invoke(() => description = PrimaryPresentation is null
            ? "вывод выключен"
            : PrimaryPresentation.Describe() + " | помеха: " + (lastPresentIssue ?? "нет"));

        return description;
    }

    /// <summary>Сколько раз клиент прислал кадр.</summary>
    public int CommittedFrameCount => committedFrames;

    internal void NoteCommit() => Interlocked.Increment(ref committedFrames);

    /// <summary>Подача событий ввода клиентам.</summary>
    public WaylandInput Input { get; private set; } = null!;

    /// <summary>
    /// Поднимает композитор на собственном сокете.
    /// </summary>
    /// <remarks>
    /// Имя сокета выбирается свободным: композиторов может работать несколько, по одному на
    /// браузер, и они не должны мешать друг другу.
    /// </remarks>
    public static WaylandCompositor Create(WaylandCompositorSettings? settings = null)
    {
        var effectiveSettings = settings ?? new WaylandCompositorSettings();
        var runtimeDirectory = ResolveRuntimeDirectory();

        Directory.CreateDirectory(runtimeDirectory);

        var (socket, path) = BindFreeSocket(runtimeDirectory);
        var compositor = new WaylandCompositor(socket, path, effectiveSettings);

        compositor.Input = new WaylandInput(compositor);
        compositor.RegisterGlobals();

        if (effectiveSettings.EnablePresentation)
        {
            // Первое хост-окно открывается сразу; следующие создаются лениво по числу окон
            // браузера — каждое становится самостоятельной записью в таскбаре системы.
            compositor.EnsurePresentationWindow(effectiveSettings, PrimaryWindowIndex);
        }

        compositor.eventLoop = Task.Run(() => compositor.RunEventLoopAsync(compositor.lifetime.Token));

        return compositor;
    }

    /// <summary>
    /// Показывает кадр клиента в окне вывода.
    /// </summary>
    /// <remarks>
    /// Без включённого вывода вызов ничего не стоит: буфер даже не отображён в память.
    /// </remarks>
    /// <summary>Помечает сцену устаревшей.</summary>
    internal void InvalidateScene() => isSceneDirty = true;

    internal void PresentScene()
    {
        isSceneDirty = false;

        if (presentations.Count == 0)
            return;

        var started = Stopwatch.GetTimestamp();

        // ★ Группируем слои по КОРНЕВОЙ поверхности: каждый root Chrome-окна обслуживается
        // СВОИМ хост-окном — отдельная запись в таскбаре системы, независимый фокус и заголовок.
        var groupByWindow = new Dictionary<int, List<Presentation.WaylandSceneLayer>>();

        foreach (var client in clients)
        {
            foreach (var surface in client.OfType<WaylandSurface>())
            {
                if (TryBuildLayer(client, surface) is not { } layer || layer.WindowIndex < 0)
                    continue;

                if (!groupByWindow.TryGetValue(layer.WindowIndex, out var group))
                {
                    group = [];
                    groupByWindow[layer.WindowIndex] = group;
                }

                group.Add(layer);
            }
        }

        if (groupByWindow.Count == 0)
            return;

        // Окно с номером N показывается в хост-окне N: создаются ровно те окна, которым есть что показывать.
        foreach (var windowIndex in groupByWindow.Keys)
            EnsurePresentationWindow(Settings, windowIndex);

        // ★ Порядок обхода таблицы объектов произволен, а слои обязаны идти снизу вверх: окно,
        // затем вложенные поверхности, затем всплывающие. Firefox рисует содержимое в
        // подповерхность и оставляет само окно пустым — при обратном порядке оно затирало бы кадр.
        var presentedAny = PresentGroups(groupByWindow);

        if (presentedAny)
        {
            _ = Interlocked.Increment(ref presentedFrames);
            sceneTicks += Stopwatch.GetTimestamp() - started;
            return;
        }

        // Сцена не показана (буфер занят или окно не готово) — повторить на следующем обороте.
        isSceneDirty = true;
        lastPresentIssue = PrimaryPresentation?.Describe();
    }

    /// <summary>
    /// Показывает группы слоёв в хост-окнах с теми же номерами; лишние окна прячет.
    /// </summary>
    /// <returns><see langword="true"/>, если хотя бы одна сцена показана.</returns>
    private bool PresentGroups(Dictionary<int, List<Presentation.WaylandSceneLayer>> layerGroups)
    {
        var presentedAny = false;

        foreach (var (windowIndex, window) in presentations)
        {
            if (!layerGroups.TryGetValue(windowIndex, out var group))
            {
                window.SetVisible(isVisible: false);
                continue;
            }

            // Свёрнутое окно возвращается, как только его слои снова есть: клиент мог пропустить
            // кадр при ресайзе, и без возврата вкладка навсегда пропадала бы из таскбара.
            window.SetVisible(isVisible: true);

            group.Sort(static (first, second) => first.Depth.CompareTo(second.Depth));

            if (window.PresentScene(group))
                presentedAny = true;
        }

        return presentedAny;
    }

    /// <summary>
    /// Держит число хост-окон не меньше числа активных корневых окон браузера: каждое —
    /// самостоятельная запись в таскбаре системы.
    /// </summary>
    private void EnsurePresentationWindow(WaylandCompositorSettings settings, int windowIndex)
    {
        if (presentations.ContainsKey(windowIndex))
            return;

        // CA2000: владение окном переходит presentations; Dispose вызывается в Dispose композитора.
#pragma warning disable CA2000
        var window = WaylandPresentationWindow.TryOpen(
            settings.Resolution,
            settings.WindowTitle ?? string.Empty,
            settings.ApplicationId);
#pragma warning restore CA2000
        if (window is null)
            return;

        window.HostInputReceived += hostEvent => ForwardHostInput(hostEvent, windowIndex);
        window.HostWindowEvent += hostEvent => ForwardHostWindowEvent(hostEvent, windowIndex);
        window.SceneRefreshRequested += () => Post(PresentScene);
        presentations[windowIndex] = window;
    }

    /// <summary>Собирает слой сцены для поверхности, если её есть чем показать.</summary>
    private static Presentation.WaylandSceneLayer? TryBuildLayer(WaylandClient client, WaylandSurface surface)
    {
        if (!surface.IsMapped || surface.Role is WaylandSurfaceRole.None or WaylandSurfaceRole.Cursor)
            return null;

        if (surface.CurrentBuffer is not { Pool: { MappedMemory: not 0 } pool } buffer)
            return null;

        var frameBytes = buffer.Stride * buffer.Height;
        if (buffer.Offset < 0 || buffer.Offset + frameBytes > pool.MappedSize)
            return null;

        var isRoot = surface.Role == WaylandSurfaceRole.Window;
        var (x, y) = ResolveOrigin(client, surface);
        var rootSurfaceId = isRoot ? surface.Id : ResolveRootSurfaceId(client, surface);

        return new Presentation.WaylandSceneLayer
        {
            RootSurfaceId = rootSurfaceId,
            WindowIndex = isRoot
                ? surface.WindowIndex
                : client.Find<WaylandSurface>(rootSurfaceId)?.WindowIndex ?? -1,
            Memory = pool.MappedMemory + buffer.Offset,
            Stride = buffer.Stride,
            Width = buffer.Width,
            Height = buffer.Height,
            Scale = surface.BufferScale,
            X = x,
            Y = y,
            // Окно задаёт размер сцены; вложенные поверхности ложатся поверх по своим смещениям.
            IsRoot = isRoot,
            IsOpaque = buffer.Format == FormatWithoutAlpha,
            Depth = surface.Role switch
            {
                WaylandSurfaceRole.Window => 0,
                WaylandSurfaceRole.Subsurface => 1,
                _ => 2,
            },
            Geometry = isRoot ? surface.WindowGeometry : null,
        };
    }

    /// <summary>
    /// Считает положение поверхности в сцене, складывая смещения по цепочке родителей.
    /// </summary>
    private static (int X, int Y) ResolveOrigin(WaylandClient client, WaylandSurface surface)
    {
        var x = surface.Position.X;
        var y = surface.Position.Y;
        var parentId = surface.ParentSurfaceId;

        // Потолок обхода защищает от закольцованной иерархии, которую мог бы прислать клиент.
        for (var depth = 0; depth < MaxSurfaceDepth && parentId != 0; ++depth)
        {
            if (client.Find<WaylandSurface>(parentId) is not { } parent)
                break;

            x += parent.Position.X;
            y += parent.Position.Y;
            parentId = parent.ParentSurfaceId;
        }

        return (x, y);
    }

    private const int MaxSurfaceDepth = 16;

    /// <summary>
    /// Поднимается по цепочке родителей до корневой поверхности (окна).
    /// </summary>
    private static uint ResolveRootSurfaceId(WaylandClient client, WaylandSurface surface)
    {
        var parentId = surface.ParentSurfaceId;
        for (var depth = 0; depth < MaxSurfaceDepth && parentId != 0; ++depth)
        {
            if (client.Find<WaylandSurface>(parentId) is not { } parent)
                break;

            if (parent.Role == WaylandSurfaceRole.Window)
                return parent.Id;

            parentId = parent.ParentSurfaceId;
        }

        return surface.Id;
    }
    private const uint FormatWithoutAlpha = 1;

    private bool isSceneDirty;

    /// <summary>
    /// Передаёт курсор браузера в сессию разработчика.
    /// </summary>
    internal unsafe void PresentCursor(WaylandBuffer buffer, int hotspotX, int hotspotY)
    {
        if (presentations.Count == 0 || buffer.Pool is not { MappedMemory: not 0 } pool)
            return;

        var frameBytes = buffer.Stride * buffer.Height;
        if (buffer.Offset + frameBytes > pool.MappedSize)
            return;

        var frame = new Presentation.WaylandFrame
        {
            Pixels = new ReadOnlySpan<byte>((byte*)pool.MappedMemory + buffer.Offset, frameBytes),
            Stride = buffer.Stride,
            Width = buffer.Width,
            Height = buffer.Height,
        };

        // Курсор назначается каждому хост-окну: браузер задаёт его один раз на сеанс, и окно без
        // назначения показывало бы стрелку оболочки вместо курсора страницы.
        foreach (var window in presentations.Values)
            window.PresentCursor(frame, hotspotX, hotspotY);

        _ = Interlocked.Increment(ref cursorUpdates);
    }

    /// <summary>Сколько раз браузер сменил курсор.</summary>
    public int CursorUpdateCount => cursorUpdates;

    /// <summary>Среднее время сборки сцены в миллисекундах.</summary>
    public double AverageSceneMilliseconds => presentedFrames == 0
        ? 0
        : (double)sceneTicks / presentedFrames / Stopwatch.Frequency * 1000;

    private long sceneTicks;



    /// <summary>Состав сцены: роли, размеры, форматы и геометрия — для диагностики.</summary>
    public string DescribeScene()
    {
        var description = "";

        Invoke(() =>
        {
            var parts = new List<string>();

            foreach (var client in clients)
            {
                foreach (var surface in client.OfType<WaylandSurface>())
                {
                    if (surface.Role == WaylandSurfaceRole.None || surface.CurrentBuffer is not { } buffer)
                        continue;

                    var geometry = surface.WindowGeometry is { } box
                        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $" геом={box.X},{box.Y} {box.Width}x{box.Height}")
                        : "";

                    parts.Add(string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"{surface.Role} {buffer.Width}x{buffer.Height} формат={buffer.Format}"
                            + $" поз={surface.Position.X},{surface.Position.Y}{geometry}"));
                }
            }

            description = string.Join(" | ", parts);
        });

        return description;
    }

    /// <summary>Сохраняет показанный кадр в файл для сравнения.</summary>
    public bool TryCaptureFrame(string path)
    {
        var captured = false;
        Invoke(() => captured = PrimaryPresentation?.TryCaptureFrame(path) ?? false);

        return captured;
    }

    /// <summary>Прозрачность углов показанной сцены.</summary>
    public string DescribeCornerAlpha()
    {
        var description = "вывод выключен";
        Invoke(() => description = PrimaryPresentation?.DescribeCornerAlpha() ?? "вывод выключен");

        return description;
    }

    /// <summary>Последний заголовок окна.</summary>
    public string? LastWindowTitle { get; private set; }

    private int cursorUpdates;

    /// <summary>Прячет курсор в сессии разработчика.</summary>
    internal void HideCursor()
    {
        foreach (var window in presentations.Values)
            window.HideCursor();
    }

    /// <summary>
    /// Переадресует запрос окна оболочке сессии разработчика.
    /// </summary>
    /// <remarks>
    /// ★ Жестовые запросы (перетаскивание, тяга за край, меню) несут номер нашего нажатия.
    /// Оболочке хоста он неизвестен, поэтому запрос имеет смысл только тогда, когда наше нажатие
    /// порождено живым нажатием в сессии — тогда окно подставляет СВОЙ номер.
    /// </remarks>
    internal void ForwardWindowCommand(Presentation.WaylandWindowCommand command, int windowIndex)
    {
        // Команда идёт СВОЕМУ хост-окну: адресация на первое делала второе окно неперетаскиваемым.
        if (!presentations.TryGetValue(windowIndex, out var window))
            return;

        if (command.RequiresGestureSerial && command.Serial != Input.LastButtonSerial)
        {
            windowCommandLog.Add(command.Kind + " ОТКЛОНЁН: serial "
                + command.Serial.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " против живого "
                + Input.LastButtonSerial.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return;
        }

        if (command.Kind == Presentation.WaylandWindowCommandKind.SetTitle)
            LastWindowTitle = command.Text;
        else if (command.Kind != Presentation.WaylandWindowCommandKind.SetAppId)
            windowCommandLog.Add(command.Kind.ToString());

        window.ApplyWindowCommand(command);
    }

    /// <summary>Запросы окна, пришедшие от браузера.</summary>
    public IReadOnlyList<string> WindowCommandLog => windowCommandLog;

    /// <summary>Состав устройств и окон по клиентам — для диагностики адресации ввода.</summary>
    public string DescribeInputTargets()
    {
        var description = "";

        Invoke(() =>
        {
            var parts = clients.Select((client, index) => string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"#{index}: окно={client.OfType<XdgToplevel>().Count()} "
                    + $"указатель={client.OfType<WaylandPointer>().Count()} "
                    + $"клавиатура={client.OfType<WaylandKeyboard>().Count()} "
                    + $"касание={client.OfType<WaylandTouch>().Count()} "
                    + $"цель={WaylandInput.DescribeFocusTarget(client)}"));

            description = string.Join(" | ", parts);
        });

        return description;
    }

    /// <summary>Состояние окна браузера: развёрнуто или нет.</summary>
    public bool IsWindowMaximized
    {
        get
        {
            var result = false;
            Invoke(() => result = clients
                .SelectMany(client => client.OfType<XdgToplevel>())
                .Any(toplevel => toplevel.IsMaximized));

            return result;
        }
    }

    /// <summary>
    /// Геометрия окна по его ПОРЯДКОВОМУ индексу (порядок создания окон браузера):
    /// surface-id композитора наружу не торчит, а индекс устойчив — окна создаются последовательно.
    /// </summary>
    public (int X, int Y, int Width, int Height) GetWindowGeometryByIndex(int windowIndex)
    {
        // ★ Смещение прибавляется В ЛЮБОМ случае: до первого set_window_geometry размер окна
        // неизвестен, но место в каскаде — известно. Без него клик во второе окно уходил бы
        // в координаты первого, а вызывающий считал бы ответ достоверным.
        var (offsetX, offsetY) = GetWindowOffset(windowIndex);
        var resolution = Settings.Resolution;
        var result = (X: offsetX, Y: offsetY, resolution.Width, resolution.Height);

        Invoke(() =>
        {
            var root = EnumerateRootSurfaces().FirstOrDefault(surface => surface.WindowIndex == windowIndex);
            if (root?.WindowGeometry is { } box)
                result = (box.X + offsetX, box.Y + offsetY, box.Width, box.Height);
        });

        return result;
    }

    /// <summary>Корневые поверхности (окна) всех клиентов в порядке обнаружения.</summary>
    private IEnumerable<WaylandSurface> EnumerateRootSurfaces()
    {
        foreach (var client in clients)
        {
            foreach (var surface in client.OfType<WaylandSurface>())
            {
                if (surface.Role == WaylandSurfaceRole.Window)
                    yield return surface;
            }
        }
    }

    /// <summary>Размер, с которым согласился браузер.</summary>
    public System.Drawing.Size WindowSize
    {
        get
        {
            var result = System.Drawing.Size.Empty;
            Invoke(() => result = clients
                .SelectMany(client => client.OfType<XdgSurface>())
                .Where(surface => surface.Toplevel is not null)
                .Select(surface => surface.RequestedSize)
                .FirstOrDefault());

            return result;
        }
    }

    /// <summary>
    /// Размер, который композитор предлагает новому окну в первом configure.
    /// </summary>
    /// <remarks>
    /// ★ Пустое значение (по умолчанию) — прежнее «решай сам»: Chrome на ноль берёт размер из
    /// своего <c lang="text">--window-size</c>. Firefox на Wayland так не умеет: <c lang="text">-width/-height</c>
    /// у него работают только под X11, а на configure(0, 0) окно встаёт в свой минимум — замер
    /// показал <c lang="text">set_window_geometry(26, 23, 500, 200)</c> и область просмотра 500×127 при
    /// запуске с <c lang="text">-width 1440 -height 900</c>. Размер ему обязан назвать композитор.
    /// </remarks>
    public System.Drawing.Size PreferredWindowSize
    {
        get
        {
            var result = System.Drawing.Size.Empty;
            Invoke(() => result = preferredWindowSize);
            return result;
        }

        set => Invoke(() => preferredWindowSize = value);
    }

    // Читается только циклом событий при первом configure; пишется через Invoke.
    private System.Drawing.Size preferredWindowSize;

    /// <summary>Размер для первого configure — без маршалинга, для вызова из цикла событий.</summary>
    internal System.Drawing.Size InitialWindowSize => preferredWindowSize;

    private readonly List<string> windowCommandLog = [];

    /// <summary>
    /// Принимает решение оболочки хоста и передаёт его браузеру.
    /// </summary>
    private void ForwardHostWindowEvent(Presentation.WaylandHostWindowEvent hostEvent, int windowIndex)
    {
        foreach (var client in clients)
        {
            foreach (var surface in client.OfType<XdgSurface>())
            {
                // Решение оболочки касается только своего окна: иначе закрытие или ресайз одного
                // разойдётся по всем окнам браузера.
                if (surface.Toplevel is not { } toplevel || toplevel.WindowIndex != windowIndex)
                    continue;

                if (hostEvent.Kind == Presentation.WaylandHostWindowEventKind.Close)
                {
                    toplevel.SendClose();
                    continue;
                }

                if (hostEvent.Size.Width <= 0 || hostEvent.Size.Height <= 0)
                    continue;

                surface.RequestedSize = hostEvent.Size;
                surface.SendCurrentConfigure();
            }
        }
    }

    /// <summary>
    /// Подаёт ввод по пути сессии разработчика, минуя саму сессию.
    /// </summary>
    /// <remarks>
    /// ★ Только для проверки. Повторяет ровно тот путь, по которому идёт настоящая мышь хоста:
    /// подача напрямую через <see cref="Input"/> его не задевает и потому ничего о нём не доказывает.
    /// </remarks>
    /// <param name="kind">Вид события: 0 — движение, 1 — кнопка, 2 — колесо, 3 — уход указателя.</param>
    /// <param name="x">Координата по горизонтали.</param>
    /// <param name="y">Координата по вертикали.</param>
    /// <param name="code">Код кнопки, клавиши или номер оси.</param>
    /// <param name="value">Величина прокрутки.</param>
    /// <param name="isPressed">Нажатие, а не отпускание.</param>
    public void SimulateHostInput(int kind, double x = 0, double y = 0, uint code = 0, double value = 0, bool isPressed = false)
        => ForwardHostInput(new WaylandHostInputEvent
        {
            Kind = (WaylandHostInputKind)kind,
            X = x,
            Y = y,
            Code = code,
            Value = value,
            IsPressed = isPressed,
        });

    /// <summary>
    /// Переводит системный ввод разработчика в события для браузера.
    /// </summary>
    private void ForwardHostInput(WaylandHostInputEvent hostEvent, int windowIndex = 0)
    {
        // ★ Координаты хост-события локальны для СВОЕГО окна; общее пространство ввода — каскад:
        // прибавляем смещение окна-источника, маршрутизация по точке потом вычтет его обратно.
        var (offsetX, offsetY) = GetWindowOffset(windowIndex);

        switch (hostEvent.Kind)
        {
            case WaylandHostInputKind.PointerMotion:
                Input.MoveTo(hostEvent.X + offsetX, hostEvent.Y + offsetY);
                break;

            case WaylandHostInputKind.PointerButton:
                // ★ Нажатие и отпускание передаются как есть. Синтез щелчка с собственной паузой
                // сделал бы невозможным удержание кнопки: перетаскивание и выделение текста не работали бы.
                // Кнопка адресуется окну под текущим указателем: сначала подводим его в окно-источник.
                Input.MoveTo(hostEvent.X + offsetX, hostEvent.Y + offsetY);
                Input.SendPointerButton((WaylandPointerButton)hostEvent.Code, hostEvent.IsPressed);
                break;

            case WaylandHostInputKind.PointerAxis:
                // ★ Событие оси не несёт координат: подводка указателя увела бы его в угол окна.
                // Позиция уже актуальна от последнего движения.
                Input.SendPointerAxis(hostEvent.Code, hostEvent.Value);
                break;

            case WaylandHostInputKind.PointerLeave:
                Input.SendPointerLeave(windowIndex);
                break;

            case WaylandHostInputKind.KeyboardFocus:
                Input.SetKeyboardFocus(hostEvent.IsPressed, windowIndex);
                break;

            case WaylandHostInputKind.Key:
                Input.SendKey(hostEvent.Code, hostEvent.IsPressed);
                break;

            case WaylandHostInputKind.Modifiers:
                Input.SendModifiers(hostEvent.Code, hostEvent.Latched, hostEvent.Locked, hostEvent.Group);
                break;
        }
    }

    /// <summary>
    /// Выполняет действие в цикле событий композитора.
    /// </summary>
    /// <remarks>
    /// ★ Протокол без очередности невозможен: таблица объектов меняется при разборе запросов, а
    /// сообщения идут по одному сокету. Подача ввода со стороны перемешивала бы байты с чужим
    /// ответом — клиент считает это нарушением и рвёт связь.
    /// </remarks>
    internal void Post(Action action)
    {
        pendingActions.Enqueue(action);

        // Вызов из самого цикла выполнится на ближайшем обороте, рекурсии не будет.
        if (Environment.CurrentManagedThreadId == eventLoopThreadId)
            DrainPendingActions();
    }

    /// <summary>Ждёт выполнения действия в цикле событий.</summary>
    internal void Invoke(Action action)
    {
        if (Environment.CurrentManagedThreadId == eventLoopThreadId)
        {
            action();
            return;
        }

        using var completed = new ManualResetEventSlim(initialState: false);

        pendingActions.Enqueue(() =>
        {
            try
            {
                action();
            }
            finally
            {
                completed.Set();
            }
        });

        _ = completed.Wait(ActionTimeout, lifetime.Token);
    }

    private void DrainPendingActions()
    {
        while (pendingActions.TryDequeue(out var action))
            action();
    }

    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> pendingActions = new();
    private int eventLoopThreadId;
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Выдаёт следующий порядковый номер события.</summary>
    internal uint NextSerial() => Interlocked.Increment(ref nextSerial);

    /// <summary>
    /// Привязывает интерфейс к новому объекту клиента.
    /// </summary>
    internal void BindGlobal(WaylandClient client, uint name, string interfaceName, uint version, uint newId)
    {
        ArgumentNullException.ThrowIfNull(client);

        // Имя интерфейса клиент присылает для сверки; адресует же он по номеру из каталога.
        var global = globals.FirstOrDefault(candidate => candidate.Name == name
            && string.Equals(candidate.InterfaceName, interfaceName, StringComparison.Ordinal));

        if (global is null)
            return;

        // Версия не может быть выше объявленной: клиент вправе запросить меньшую.
        var effectiveVersion = Math.Min(version, global.Version);
        var bound = global.Factory(client, newId, effectiveVersion);

        client.Register(bound);
        SendInitialState(bound);
    }

    /// <summary>
    /// Отправляет описание сразу после привязки.
    /// </summary>
    /// <remarks>
    /// Клиент ждёт его перед первым кадром: без форматов буфера ему нечем рисовать, без размеров
    /// выхода некуда, а без состава seat он не заводит устройства ввода вовсе.
    /// </remarks>
    private void SendInitialState(WaylandObject bound)
    {
        switch (bound)
        {
            case WaylandShm shm:
                shm.SendFormats();
                break;

            case WaylandOutput output:
                output.SendConfiguration();
                break;

            case WaylandSeat seat:
                seat.SendCapabilities(Settings.HasTouch);
                break;
        }
    }

    private void RegisterGlobals()
    {
        // Порядок повторяет привычный для настоящих композиторов: сперва основа, затем оболочка.
        // ★ Версии намеренно НЕВЫСОКИЕ: каждая старше добавляет события, которые композитор обязан
        // присылать, иначе клиент рвёт соединение с «недопустимым аргументом». Берём минимум, при
        // котором браузер работает, — расширять можно по мере необходимости.
        AddGlobal("wl_compositor", 4, (client, id, version) => new WaylandCompositorGlobal { Id = id, Version = version, Client = client });
        AddGlobal("wl_subcompositor", 1, (client, id, version) => new WaylandSubcompositor { Id = id, Version = version, Client = client });
        AddGlobal("wl_shm", 1, (client, id, version) => new WaylandShm { Id = id, Version = version, Client = client });

        // ★ Обмена данными мы не ведём, но без этого интерфейса GTK не создаёт seat, и браузер
        // отбрасывает весь ввод — мышь и клавиатуру разом.
        AddGlobal("wl_data_device_manager", 3, (client, id, version) => new WaylandDataDeviceManager { Id = id, Version = version, Client = client });
        AddGlobal("wl_output", 2, (client, id, version) => new WaylandOutput { Id = id, Version = version, Client = client });
        AddGlobal("wl_seat", 5, (client, id, version) => new WaylandSeat { Id = id, Version = version, Client = client });
        AddGlobal("xdg_wm_base", 2, (client, id, version) => new XdgWmBase { Id = id, Version = version, Client = client });

        // ★ Без этого интерфейса клиент рисует свою рамку с тенью: поверхность выходит больше окна,
        // и outerWidth оказывается шире заявленного профилем размера.
        AddGlobal("zxdg_decoration_manager_v1", 1, (client, id, version) => new XdgDecorationManager { Id = id, Version = version, Client = client });
    }

    private void AddGlobal(string interfaceName, uint version, Func<WaylandClient, uint, uint, WaylandObject> factory)
        => globals.Add(new WaylandGlobal
        {
            Name = nextGlobalName++,
            InterfaceName = interfaceName,
            Version = version,
            Factory = factory,
        });

    private async Task RunEventLoopAsync(CancellationToken cancellationToken)
    {
        eventLoopThreadId = Environment.CurrentManagedThreadId;

        while (!cancellationToken.IsCancellationRequested)
        {
            AcceptPendingClients();
            ServeClients();

            // ★ Обход по снимку, а не по живой коллекции: ответ хоста внутри Pump доходит до
            // PresentScene, а тот открывает новое хост-окно — изменённая коллекция рвала цикл
            // событий исключением, и композитор замирал целиком при появлении второго окна.
            pumpBuffer.Clear();
            pumpBuffer.AddRange(presentations.Values);

            foreach (var window in pumpBuffer)
                window.Pump();

            DrainPendingActions();

            if (isSceneDirty)
                PresentScene();

            // Опрос вместо ожидания на сокете: клиентов единицы, а задержка в миллисекунду
            // незаметна на фоне частоты кадров браузера.
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private void AcceptPendingClients()
    {
        while (listener.Poll(0, SelectMode.SelectRead))
        {
            var accepted = listener.Accept();
            accepted.Blocking = false;

            var connection = new WaylandConnection(accepted);
            var client = new WaylandClient(connection, this);

            // wl_display известен клиенту заранее и создаётся до первого сообщения.
            client.Register(new WaylandDisplay { Id = WaylandDisplay.WellKnownId, Version = 1, Client = client });
            clients.Add(client);
        }
    }

    private void ServeClients()
    {
        for (var index = clients.Count - 1; index >= 0; --index)
        {
            var client = clients[index];
            client.ProcessPendingRequests();

            if (!client.IsClosed)
                continue;

            // Ввод помнит поверхности клиента: без забвения они держали бы фокус и вход указателя
            // за уже отключённым соединением.
            Input.ForgetClient(client);
            client.Dispose();
            clients.RemoveAt(index);
        }

        ReleaseUnusedPresentations();
    }

    /// <summary>
    /// Закрывает хост-окна, которым больше не отвечает ни одно окно браузера.
    /// </summary>
    /// <remarks>
    /// Окна адресуются номером, а не позицией, поэтому закрыть можно любое — соседи сохраняют
    /// свои номера, и ввод остаётся связанным с выводом.
    /// </remarks>
    private void ReleaseUnusedPresentations()
    {
        if (presentations.Count <= 1)
            return;

        var liveWindows = new HashSet<int>();

        foreach (var client in clients)
        {
            foreach (var toplevel in client.OfType<XdgToplevel>())
                _ = liveWindows.Add(toplevel.WindowIndex);
        }

        // Первое окно живёт до конца сеанса: оно держит связь с оболочкой хоста.
        foreach (var windowIndex in presentations.Keys.Where(key => key != PrimaryWindowIndex && !liveWindows.Contains(key)).ToList())
        {
            presentations[windowIndex].Dispose();
            _ = presentations.Remove(windowIndex);
        }
    }

    private static (Socket Socket, string Path) BindFreeSocket(string runtimeDirectory)
    {
        for (var index = 0; index < MaxDisplayProbe; ++index)
        {
            var path = Path.Combine(runtimeDirectory, "wayland-atom-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));

            // Сокет прошлого запуска мог остаться после аварийного завершения: занятость
            // проверяется попыткой привязки, а не наличием файла.
            if (File.Exists(path) && !TryRemoveStaleSocket(path))
                continue;

            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

            try
            {
                socket.Bind(new UnixDomainSocketEndPoint(path));
                socket.Listen(MaxPendingConnections);
                socket.Blocking = false;
                return (socket, path);
            }
            catch (SocketException)
            {
                socket.Dispose();
            }
        }

        throw new WaylandProtocolException("Не удалось занять сокет для композитора.");
    }

    private static bool TryRemoveStaleSocket(string path)
    {
        try
        {
            using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            probe.Connect(new UnixDomainSocketEndPoint(path));

            // Подключение удалось — сокет живой, занимать нельзя.
            return false;
        }
        catch (SocketException)
        {
            File.Delete(path);
            return true;
        }
    }

    private static string ResolveRuntimeDirectory()
        => Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime
            ? runtime
            : Path.Combine(Path.GetTempPath(), "atom-wayland");

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) is not 0)
            return;

        await lifetime.CancelAsync().ConfigureAwait(false);

        if (eventLoop is { } loop)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Штатная остановка цикла событий.
            }
        }

        foreach (var client in clients)
            client.Dispose();

        clients.Clear();
        foreach (var window in presentations.Values)
            window.Dispose();
        presentations.Clear();
        listener.Dispose();
        lifetime.Dispose();

        try
        {
            File.Delete(SocketPath);
        }
        catch (IOException)
        {
            // Каталог мог быть удалён раньше композитора — это не мешает остановке.
        }
    }

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1);
    private const int MaxPendingConnections = 8;
    private const int MaxDisplayProbe = 64;
}

/// <summary>
/// Объявленный интерфейс композитора.
/// </summary>
internal sealed record WaylandGlobal
{
    /// <summary>Номер интерфейса в каталоге.</summary>
    public required uint Name { get; init; }

    /// <summary>Имя интерфейса.</summary>
    public required string InterfaceName { get; init; }

    /// <summary>Наибольшая поддерживаемая версия.</summary>
    public required uint Version { get; init; }

    /// <summary>Создаёт объект при привязке клиентом.</summary>
    public required Func<WaylandClient, uint, uint, WaylandObject> Factory { get; init; }
}
