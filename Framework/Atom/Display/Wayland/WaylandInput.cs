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

    // Цель активного касания: поверхность, получившая down, со своим каскадным смещением —
    // motion и up обязаны прийти в ту же поверхность, куда пришёл down.
    private readonly Dictionary<int, (Protocol.WaylandClient Client, uint SurfaceId, double LocalX, double LocalY, int OffsetX, int OffsetY)> touchTargets = [];

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

        var delivered = new HashSet<(Protocol.WaylandClient Client, uint SurfaceId)>();

        foreach (var (client, surfaceId, localX, localY, _, _) in ResolvePointerTargets(x, y))
        {
            delivered.Add((client, surfaceId));

            foreach (var pointer in client.OfType<WaylandPointer>())
            {
                EnsureEntered(client, pointer, surfaceId, localX, localY);
                pointer.SendMotion(timestamp, localX, localY);
                pointer.SendFrame();
                ++DeliveredPointerEvents;
            }
        }

        // Уход с поверхностей, которые указатель покинул: без leave окно держит подсветку
        // под курсором навсегда и не отпускает перетаскивание при переходе в соседнее окно.
        var stale = enteredSurfaces.Where(key => !delivered.Contains(key)).ToList();
        if (stale.Count == 0)
            return;

        var leaveSerial = compositor.NextSerial();

        foreach (var (client, surfaceId) in stale)
        {
            enteredSurfaces.Remove((client, surfaceId));

            foreach (var pointer in client.OfType<WaylandPointer>())
            {
                pointer.SendLeave(leaveSerial, surfaceId);
                pointer.SendFrame();
            }
        }
    });

    /// <summary>
    /// Маршрутизирует точку дисплея в корневые окна, которые её содержат.
    /// </summary>
    /// <remarks>
    /// ★ Общее пространство дисплея — каскад: окна сдвинуты на смещение своего индекса, а xdg-клиент
    /// считает координаты от своего (0,0), поэтому при доставке смещение вычитается. Событие получает
    /// ТОЛЬКО окно под точкой: широковещательная доставка доезжала до обоих окон, и второе окно
    /// реагировало на чужой клик. Окно без известной геометрии (ещё не сконфигурировано) принимает
    /// точки лишь пока оно первое — иначе оно перехватило бы ввод, адресованный соседям.
    /// </remarks>
    private IEnumerable<(Protocol.WaylandClient Client, uint SurfaceId, double LocalX, double LocalY, int OffsetX, int OffsetY)> ResolvePointerTargets(
        double x, double y)
    {
        foreach (var (client, surfaceId, _, offsetX, offsetY) in EnumerateRootTargets())
        {
            var rootSurface = client.Find<Objects.WaylandSurface>(surfaceId);

            // ★ До первого set_window_geometry размер окна неизвестен — берём разрешение дисплея.
            // Безусловный захват любой точки вернул бы тот же дефект с обратным знаком: окно без геометрии
            // перехватывало бы клики, адресованные соседям.
            var geometry = rootSurface?.WindowGeometry;
            var width = geometry is { Width: > 0 } ? geometry.Value.Width : compositor.Settings.Resolution.Width;
            var height = geometry is { Height: > 0 } ? geometry.Value.Height : compositor.Settings.Resolution.Height;

            var contains = x >= offsetX && y >= offsetY
                && x < offsetX + width && y < offsetY + height;

            if (contains)
                yield return (client, surfaceId, x - offsetX, y - offsetY, offsetX, offsetY);
        }
    }

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

        // Кнопка уходит ТОЛЬКО клиенту, в чьём окне находится указатель: широковещательное
        // нажатие заставляло второе окно реагировать на чужой клик.
        foreach (var (client, surfaceId, localX, localY, _, _) in ResolvePointerTargets(lastPointerX, lastPointerY))
        {
            foreach (var pointer in client.OfType<WaylandPointer>())
            {
                // Нажатие без предварительного входа клиент отбрасывает: он не знает целевой поверхности.
                EnsureEntered(client, pointer, surfaceId, localX, localY);

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

        // Прокрутка уходит окну под указателем: соседнее окно колесо не касается.
        foreach (var (client, surfaceId, localX, localY, _, _) in ResolvePointerTargets(lastPointerX, lastPointerY))
        {
            foreach (var pointer in client.OfType<WaylandPointer>())
            {
                EnsureEntered(client, pointer, surfaceId, localX, localY);

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
    internal void SendPointerLeave(int windowIndex) => compositor.Post(() =>
    {
        var serial = compositor.NextSerial();

        foreach (var (client, surfaceId, _, _, _) in EnumerateRootTargets().Where(target => target.WindowIndex == windowIndex))
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
    public void SetKeyboardFocus(bool hasFocus, int? windowIndex = null) => compositor.Post(() =>
    {
        // Фокус — состояние, а не разовое событие: окно могло ещё не появиться к этому моменту.
        wantsKeyboardFocus = hasFocus;

        // ★ Окно называет только оболочка хоста. Драйвер запрашивает фокус без номера, и молчаливый
        // ноль перебивал бы выбор хоста: печать во второе окно уходила бы в первое.
        if (windowIndex is { } requested)
            keyboardFocusIndex = requested;

        ApplyKeyboardFocus();
    });

    private void ApplyKeyboardFocus()
    {
        var hasFocus = wantsKeyboardFocus;

        // Клавиатура одна на сеанс: фокус получает окно с заданным номером — события с чужих
        // окон клавиатуру не переключают.
        foreach (var (client, surfaceId, _, _, _) in EnumerateRootTargets().Where(target => target.WindowIndex == keyboardFocusIndex))
        {
            // Отметка ставится на поверхность, а не на устройство: клавиатур у клиента бывает несколько.
            if (hasFocus
                ? !focusedSurfaces.Add((client, surfaceId))
                : !focusedSurfaces.Remove((client, surfaceId)))
            {
                continue;
            }

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
    private int keyboardFocusIndex;

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

        // Модификаторы — часть клавиатурного состояния и идут только окну с фокусом: рассылка всем
        // оставляла бы в соседнем окне зажатыми Shift и Ctrl, которых там никто не нажимал.
        foreach (var (client, surfaceId) in EnumerateFocusTargets())
        {
            if (!focusedSurfaces.Contains((client, surfaceId)))
                continue;

            foreach (var keyboard in client.OfType<WaylandKeyboard>())
                keyboard.SendModifiers(serial, depressed, latched, locked, group);
        }
    });

    /// <summary>
    /// Забывает состояние ввода отключившегося клиента.
    /// </summary>
    /// <remarks>
    /// Записи о входе и фокусе держат ссылку на клиента: без очистки они пережили бы соединение,
    /// а новый клиент с теми же номерами поверхностей не получил бы ни входа, ни фокуса.
    /// </remarks>
    internal void ForgetClient(Protocol.WaylandClient client)
    {
        _ = enteredSurfaces.RemoveWhere(key => key.Client == client);
        _ = focusedSurfaces.RemoveWhere(key => key.Client == client);

        foreach (var touchId in touchTargets.Where(pair => pair.Value.Client == client).Select(pair => pair.Key).ToList())
            _ = touchTargets.Remove(touchId);
    }

    /// <summary>
    /// Забывает состояние ввода уничтоженной поверхности.
    /// </summary>
    /// <remarks>
    /// Клиент переиспользует освободившиеся номера объектов: без забвения вход в новую поверхность
    /// с тем же номером считался бы уже состоявшимся, и указатель молчал бы.
    /// </remarks>
    internal void ForgetSurface(Protocol.WaylandClient client, uint surfaceId)
    {
        _ = enteredSurfaces.Remove((client, surfaceId));

        // Фокус возвращается первому окну: указатель на закрытое окно оставил бы клавиатуру без цели.
        if (focusedSurfaces.Remove((client, surfaceId)))
            keyboardFocusIndex = 0;

        foreach (var touchId in touchTargets
            .Where(pair => pair.Value.Client == client && pair.Value.SurfaceId == surfaceId)
            .Select(pair => pair.Key)
            .ToList())
        {
            _ = touchTargets.Remove(touchId);
        }
    }

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

        // Касание адресуется окну под точкой и запоминается: motion и up идут в ту же поверхность.
        foreach (var target in ResolvePointerTargets(x, y).Take(1))
        {
            touchTargets[touchId] = target;

            foreach (var touch in target.Client.OfType<WaylandTouch>())
            {
                touch.SendDown(serial, timestamp, target.SurfaceId, touchId, target.LocalX, target.LocalY);
                touch.SendFrame();
            }
        }
    });

    private void SendTouchMotion(int touchId, double x, double y) => compositor.Post(() =>
    {
        if (!touchTargets.TryGetValue(touchId, out var target))
            return;

        var timestamp = Timestamp();

        foreach (var touch in target.Client.OfType<WaylandTouch>())
        {
            touch.SendMotion(timestamp, touchId, x - target.OffsetX, y - target.OffsetY);
            touch.SendFrame();
        }
    });

    private void SendTouchUp(int touchId) => compositor.Post(() =>
    {
        if (!touchTargets.TryGetValue(touchId, out var target))
            return;

        touchTargets.Remove(touchId);

        var timestamp = Timestamp();
        var serial = compositor.NextSerial();

        foreach (var touch in target.Client.OfType<WaylandTouch>())
        {
            touch.SendUp(serial, timestamp, touchId);
            touch.SendFrame();
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
            //
            // ★ Окон у одного клиента бывает несколько: второе окно браузера — второй toplevel того
            // же соединения. Перечисляются ВСЕ корневые окна: выбор первого доставлял бы ввод
            // второго окна всегда первому.
            foreach (var toplevel in client.OfType<XdgToplevel>())
            {
                if (client.Find<WaylandSurface>(toplevel.OwnerSurfaceId) is not null)
                    yield return (client, toplevel.OwnerSurfaceId);
            }
        }
    }

    /// <summary>
    /// Корневые окна с их номерами и каскадными смещениями.
    /// </summary>
    /// <remarks>
    /// ★ Номер берётся у самого окна, а не из порядка обхода: таблица объектов клиента — словарь,
    /// её порядок произволен и меняется при удалении объектов. Нумерация по месту в обходе
    /// переставляла окна местами, и ввод уходил не в то окно.
    /// </remarks>
    private IEnumerable<(Protocol.WaylandClient Client, uint SurfaceId, int WindowIndex, int OffsetX, int OffsetY)> EnumerateRootTargets()
    {
        foreach (var client in compositor.Clients)
        {
            foreach (var toplevel in client.OfType<XdgToplevel>().OrderBy(candidate => candidate.WindowIndex))
            {
                if (client.Find<WaylandSurface>(toplevel.OwnerSurfaceId) is null)
                    continue;

                var (offsetX, offsetY) = compositor.GetWindowOffset(toplevel.WindowIndex);
                yield return (client, toplevel.OwnerSurfaceId, toplevel.WindowIndex, offsetX, offsetY);
            }
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
