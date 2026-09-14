using System.Runtime.Versioning;

using Atom.Display.Wayland.Objects;

namespace Atom.Display.Wayland;

/// <summary>
/// Подача событий ввода клиентам композитора.
/// </summary>
/// <remarks>
/// ★ Здесь исчезает вся прежняя обвязка. Раньше ввод шёл окольным путём: создать устройство в
/// ядре через <c lang="text">/dev/uinput</c>, дождаться udev, добиться, чтобы композитор его подхватил,
/// и надеяться, что libinput правильно пересчитает координаты. Отсюда брались и права root, и
/// udev-правила, и служба seat, и требование совпадения матрицы устройства с размером выхода.
///
/// Композитор наш, поэтому событие подаётся прямо в протокол — ровно теми же сообщениями, какие
/// клиент получил бы от настоящего устройства. Для страницы разницы нет: событие приходит от
/// оконной системы, а значит с <c lang="text">isTrusted = true</c>.
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class WaylandInput
{
    private readonly WaylandCompositor compositor;
    private readonly HashSet<(Protocol.WaylandClient Client, uint SurfaceId)> enteredSurfaces = [];
    private readonly HashSet<(Protocol.WaylandClient Client, uint SurfaceId)> focusedSurfaces = [];
    private int nextTouchId;

    internal WaylandInput(WaylandCompositor compositor) => this.compositor = compositor;

    /// <summary>
    /// Порядковый номер последнего нажатия, выданный клиенту.
    /// </summary>
    /// <remarks>
    /// ★ Клиент предъявляет его в <c lang="text">xdg_toplevel.move</c> и <c lang="text">resize</c>: оболочка
    /// обязана проверить, что жест начат живым вводом. Мы по нему находим соответствующий
    /// номер в сессии хоста — без этого настоящая оболочка запрос отклонит.
    /// </remarks>
    internal uint LastButtonSerial { get; private set; }

    /// <summary>
    /// Касание в точке экрана.
    /// </summary>
    /// <param name="x">Координата по горизонтали.</param>
    /// <param name="y">Координата по вертикали.</param>
    /// <param name="holdDuration">Сколько держится палец.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask TapAsync(double x, double y, TimeSpan holdDuration = default, CancellationToken cancellationToken = default)
    {
        var touchId = Interlocked.Increment(ref nextTouchId);

        SendTouchDown(touchId, x, y);
        await Task.Delay(holdDuration > TimeSpan.Zero ? holdDuration : DefaultHoldDuration, cancellationToken).ConfigureAwait(false);
        SendTouchUp(touchId);
    }

    /// <summary>
    /// Проведение пальцем по траектории без отрыва.
    /// </summary>
    public async ValueTask SwipeAsync(IReadOnlyList<(double X, double Y)> path, TimeSpan duration = default, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.Count < 2)
            throw new ArgumentException("Для проведения нужны минимум две точки.", nameof(path));

        var touchId = Interlocked.Increment(ref nextTouchId);
        var total = duration > TimeSpan.Zero ? duration : DefaultSwipeDuration;
        var stepDelay = TimeSpan.FromMilliseconds(total.TotalMilliseconds / (path.Count - 1));

        SendTouchDown(touchId, path[0].X, path[0].Y);

        for (var index = 1; index < path.Count; ++index)
        {
            await Task.Delay(stepDelay, cancellationToken).ConfigureAwait(false);
            SendTouchMotion(touchId, path[index].X, path[index].Y);
        }

        SendTouchUp(touchId);
    }

    /// <summary>
    /// Щелчок указателем в точке экрана.
    /// </summary>
    public async ValueTask ClickAsync(double x, double y, WaylandPointerButton button = WaylandPointerButton.Left, CancellationToken cancellationToken = default)
    {
        MoveTo(x, y);

        // Пауза между нажатием и отпусканием: мгновенный щелчок отличается от человеческого.
        await Task.Delay(ClickHoldDuration, cancellationToken).ConfigureAwait(false);

        SendPointerButton(button, pressed: true);
        await Task.Delay(ClickHoldDuration, cancellationToken).ConfigureAwait(false);
        SendPointerButton(button, pressed: false);
    }

    /// <summary>
    /// Перемещает указатель, не нажимая кнопок.
    /// </summary>
    /// <remarks>
    /// ★ <c lang="text">enter</c> шлётся РОВНО ОДИН РАЗ на вход в поверхность. Повторный вход для
    /// клиента означает новый сеанс указателя: он сбрасывает состояние кнопок и бросает начатые
    /// жесты — перетаскивание за заголовок рвётся на первом же движении мыши.
    /// </remarks>
    public void MoveTo(double x, double y) => compositor.Post(() =>
    {
        var timestamp = Timestamp();
        lastPointerX = x;
        lastPointerY = y;

        foreach (var (client, surfaceId) in EnumerateFocusTargets())
        {
            foreach (var pointer in client.OfType<WaylandPointer>())
            {
                EnsureEntered(client, pointer, surfaceId, x, y);
                pointer.SendMotion(timestamp, x, y);
                pointer.SendFrame();
                ++DeliveredPointerEvents;
            }
        }
    });

    private double lastPointerX;
    private double lastPointerY;

    /// <summary>Сколько событий указателя фактически ушло клиентам.</summary>
    public int DeliveredPointerEvents { get; private set; }

    /// <summary>
    /// Нажимает кнопку и удерживает её.
    /// </summary>
    /// <remarks>Нужно для перетаскивания и выделения: щелчок целиком здесь не годится.</remarks>
    public void PointerDown(WaylandPointerButton button = WaylandPointerButton.Left)
        => SendPointerButton(button, pressed: true);

    /// <summary>Отпускает удерживаемую кнопку.</summary>
    public void PointerUp(WaylandPointerButton button = WaylandPointerButton.Left)
        => SendPointerButton(button, pressed: false);

    /// <summary>Сообщает клиенту о состоянии кнопки без синтеза пары.</summary>
    internal void SendPointerButton(WaylandPointerButton button, bool pressed) => compositor.Post(() =>
    {
        var timestamp = Timestamp();
        var serial = compositor.NextSerial();

        if (pressed)
            LastButtonSerial = serial;

        foreach (var (client, surfaceId) in EnumerateFocusTargets())
        {
            foreach (var pointer in client.OfType<WaylandPointer>())
            {
                // Нажатие без предварительного входа клиент отбрасывает: он не знает целевой поверхности.
                EnsureEntered(client, pointer, surfaceId, lastPointerX, lastPointerY);

                pointer.SendButton(serial, timestamp, (uint)button, pressed);
                pointer.SendFrame();
            }
        }
    });

    /// <summary>
    /// Передаёт прокрутку колесом.
    /// </summary>
    /// <param name="axis">0 — вертикаль, 1 — горизонталь.</param>
    /// <param name="value">Сдвиг в точках поверхности.</param>
    public void SendPointerAxis(uint axis, double value) => compositor.Post(() =>
    {
        var timestamp = Timestamp();

        foreach (var (client, _) in EnumerateFocusTargets())
        {
            foreach (var pointer in client.OfType<WaylandPointer>())
            {
                // ★ Без источника и числа щелчков браузер считает прокрутку тачпадной и отдаёт
                // странице плавные дробные дельты — при заявленном настольном оборудовании это расхождение.
                pointer.SendAxisSource(AxisSourceWheel);
                pointer.SendAxisDiscrete(axis, value >= 0 ? 1 : -1);
                pointer.SendAxis(timestamp, axis, value);
                pointer.SendFrame();
            }
        }
    });

    /// <summary>
    /// Сообщает об уходе указателя из окна.
    /// </summary>
    /// <remarks>
    /// Без <c lang="text">leave</c> браузер держит подсветку кнопки под курсором навсегда.
    /// </remarks>
    internal void SendPointerLeave() => compositor.Post(() =>
    {
        if (enteredSurfaces.Count == 0)
            return;

        var serial = compositor.NextSerial();

        foreach (var (client, surfaceId) in EnumerateFocusTargets())
        {
            if (!enteredSurfaces.Remove((client, surfaceId)))
                continue;

            foreach (var pointer in client.OfType<WaylandPointer>())
            {
                pointer.SendLeave(serial, surfaceId);
                pointer.SendFrame();
            }
        }
    });

    /// <summary>
    /// Отдаёт окну клавиатурный фокус.
    /// </summary>
    /// <remarks>
    /// ★ Без <c lang="text">keyboard.enter</c> браузер считает окно неактивным: поля ввода не берут
    /// фокус, каретка не мигает, а часть событий отбрасывается как фоновые.
    /// </remarks>
    public void SetKeyboardFocus(bool hasFocus) => compositor.Post(() =>
    {
        // Фокус — состояние, а не разовое событие: окно могло ещё не появиться к этому моменту.
        wantsKeyboardFocus = hasFocus;
        ApplyKeyboardFocus();
    });

    private void ApplyKeyboardFocus()
    {
        var hasFocus = wantsKeyboardFocus;

        foreach (var (client, surfaceId) in EnumerateFocusTargets())
        {
            // Отметка ставится на поверхность, а не на устройство: клавиатур у клиента бывает несколько.
            if (hasFocus ? !focusedSurfaces.Add((client, surfaceId)) : !focusedSurfaces.Remove((client, surfaceId)))
                continue;

            foreach (var keyboard in client.OfType<WaylandKeyboard>())
            {
                if (!hasFocus)
                {
                    keyboard.SendLeave(compositor.NextSerial(), surfaceId);
                    continue;
                }

                keyboard.SendEnter(compositor.NextSerial(), surfaceId);

                // По спецификации состояние модификаторов обязано идти сразу за входом: иначе клиент
                // начинает с неопределённым Shift/Ctrl и первые нажатия дают не те символы.
                keyboard.SendModifiers(compositor.NextSerial(), 0, 0, 0, 0);
            }
        }
    }

    private bool wantsKeyboardFocus;

    /// <summary>
    /// Передаёт нажатие или отпускание клавиши.
    /// </summary>
    /// <param name="key">Код ядра из <c lang="text">linux/input-event-codes.h</c>.</param>
    /// <param name="pressed">Нажатие, а не отпускание.</param>
    public void SendKey(uint key, bool pressed) => compositor.Post(() =>
    {
        // Окно могло появиться после запроса фокуса — доводим состояние до текущего набора целей.
        ApplyKeyboardFocus();

        var timestamp = Timestamp();
        var serial = compositor.NextSerial();

        foreach (var (client, surfaceId) in EnumerateFocusTargets())
        {
            // Спецификация запрещает слать нажатия клавиатуре без фокуса.
            if (!focusedSurfaces.Contains((client, surfaceId)))
                continue;

            foreach (var keyboard in client.OfType<WaylandKeyboard>())
                keyboard.SendKey(serial, timestamp, key, pressed);
        }
    });

    /// <summary>Передаёт состояние модификаторов.</summary>
    internal void SendModifiers(uint depressed, uint latched, uint locked, uint group) => compositor.Post(() =>
    {
        var serial = compositor.NextSerial();

        foreach (var (client, _) in EnumerateFocusTargets())
        {
            foreach (var keyboard in client.OfType<WaylandKeyboard>())
                keyboard.SendModifiers(serial, depressed, latched, locked, group);
        }
    });

    private void EnsureEntered(Protocol.WaylandClient client, WaylandPointer pointer, uint surfaceId, double x, double y)
    {
        // ★ Ключ — пара клиент плюс поверхность: идентификаторы уникальны только внутри соединения,
        // и у второго клиента с тем же номером вход иначе не состоялся бы никогда.
        if (!enteredSurfaces.Add((client, surfaceId)))
            return;

        // Вход завершается своим кадром: он открывает сеанс указателя.
        pointer.SendEnter(compositor.NextSerial(), surfaceId, x, y);
        pointer.SendFrame();
    }

    private void SendTouchDown(int touchId, double x, double y) => compositor.Post(() =>
    {
        var timestamp = Timestamp();
        var serial = compositor.NextSerial();

        foreach (var (client, surfaceId) in EnumerateFocusTargets())
        {
            foreach (var touch in client.OfType<WaylandTouch>())
            {
                touch.SendDown(serial, timestamp, surfaceId, touchId, x, y);
                touch.SendFrame();
            }
        }
    });

    private void SendTouchMotion(int touchId, double x, double y) => compositor.Post(() =>
    {
        var timestamp = Timestamp();

        foreach (var (client, _) in EnumerateFocusTargets())
        {
            foreach (var touch in client.OfType<WaylandTouch>())
            {
                touch.SendMotion(timestamp, touchId, x, y);
                touch.SendFrame();
            }
        }
    });

    private void SendTouchUp(int touchId) => compositor.Post(() =>
    {
        var timestamp = Timestamp();
        var serial = compositor.NextSerial();

        foreach (var (client, _) in EnumerateFocusTargets())
        {
            foreach (var touch in client.OfType<WaylandTouch>())
            {
                touch.SendUp(serial, timestamp, touchId);
                touch.SendFrame();
            }
        }
    });

    /// <summary>
    /// Поверхности, которым адресуется ввод.
    /// </summary>
    /// <remarks>
    /// ★ Идентификаторы объектов уникальны только ВНУТРИ соединения. Событие с чужой поверхностью
    /// клиент считает нарушением протокола и рвёт связь («недопустимый аргумент»), поэтому событие
    /// уходит только тем клиентам, у которых есть И устройство ввода, И само окно.
    ///
    /// Браузер подключается несколькими процессами, но окно и устройства заводит один и тот же —
    /// остальные соединения служебные.
    ///
    /// ★ Окон у одного клиента бывает несколько (Firefox заводит вспомогательные), и нужно
    /// именно то, что рисует: ввод в чужое окно уходит в пустоту.
    /// </remarks>
    /// <summary>Поверхность, которой адресуется ввод этого клиента — для диагностики.</summary>
    internal static string DescribeFocusTarget(Protocol.WaylandClient client)
    {
        var toplevels = client.OfType<XdgToplevel>()
            .Select(toplevel => toplevel.OwnerSurfaceId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + (client.Find<WaylandSurface>(toplevel.OwnerSurfaceId) is null ? "(нет)" : "(есть)"));

        return string.Join('/', toplevels) is { Length: > 0 } text ? text : "нет";
    }

    private IEnumerable<(Protocol.WaylandClient Client, uint SurfaceId)> EnumerateFocusTargets()
    {
        foreach (var client in compositor.Clients)
        {
            // ★ Ввод адресуется САМОМУ ОКНУ, а не подповерхности, в которой рисуется содержимое:
            // клиент сам разводит события по вложенным поверхностям. Подповерхность здесь — чужая
            // роль, и событие с ней клиент считает нарушением протокола.
            var surfaceId = client.OfType<XdgToplevel>()
                .Select(toplevel => toplevel.OwnerSurfaceId)
                .FirstOrDefault(candidate => client.Find<WaylandSurface>(candidate) is not null);

            if (surfaceId != 0)
                yield return (client, surfaceId);
        }
    }

    /// <summary>
    /// Отметка времени события.
    /// </summary>
    /// <remarks>
    /// Протокол ждёт миллисекунды монотонных часов: по разнице между событиями клиент судит о
    /// скорости движения, и скачки назад сбивают распознавание жестов.
    /// </remarks>
    private static uint Timestamp() => (uint)(Environment.TickCount64 & 0xFFFFFFFF);

    private const uint AxisSourceWheel = 0;

    private static readonly TimeSpan DefaultHoldDuration = TimeSpan.FromMilliseconds(85);
    private static readonly TimeSpan DefaultSwipeDuration = TimeSpan.FromMilliseconds(280);
    private static readonly TimeSpan ClickHoldDuration = TimeSpan.FromMilliseconds(45);
}

/// <summary>
/// Кнопка указателя.
/// </summary>
/// <remarks>
/// Значения — коды ядра из <c lang="text">linux/input-event-codes.h</c>: протокол передаёт кнопку именно
/// ими, а не собственной нумерацией.
/// </remarks>
public enum WaylandPointerButton
{
    /// <summary>Кнопка не указана.</summary>
    None = 0,

    /// <summary>Левая кнопка.</summary>
    Left = 0x110,

    /// <summary>Правая кнопка.</summary>
    Right = 0x111,

    /// <summary>Средняя кнопка.</summary>
    Middle = 0x112,
}
