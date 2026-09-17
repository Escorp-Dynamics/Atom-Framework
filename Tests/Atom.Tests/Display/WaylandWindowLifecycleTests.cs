using System.Drawing;
using System.Runtime.Versioning;

using Atom.Display.Wayland;

namespace Atom.Tests;

/// <summary>
/// Жизненный цикл окон композитора: нумерация, маршрутизация ввода и уборка за закрытым окном.
/// </summary>
/// <remarks>
/// ★ Вывод выключен: тесты проверяют учёт окон и адресацию, а не картинку. С включённым выводом
/// каждому окну понадобилась бы живая оболочка хоста, которой на сборочной машине нет.
/// </remarks>
[TestFixture]
[Category("Display")]
[SupportedOSPlatform("linux")]
public class WaylandWindowLifecycleTests
{
    [SetUp]
    public void SetUp()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Ignore("Композитор Wayland работает только на Linux.");
    }

    [Test(Description = "Каждому окну клиента достаётся свой порядковый номер")]
    public async Task SecondWindowShouldGetOwnIndex()
    {
        await using var compositor = CreateCompositor();
        using var client = WaylandTestClient.Connect(compositor.SocketPath);

        client.CreateWindow(FirstTitle);
        client.CreateWindow(SecondTitle);
        WaitForWindows(compositor, expected: 2);

        Assert.Multiple(() =>
        {
            Assert.That(compositor.TryResolveWindowIndexByTitle(FirstTitle), Is.Zero);
            Assert.That(compositor.TryResolveWindowIndexByTitle(SecondTitle), Is.EqualTo(1));
        });
    }

    [Test(Description = "Каскадное смещение разводит окна по сцене")]
    public async Task WindowOffsetShouldCascadeByIndex()
    {
        await using var compositor = CreateCompositor();

        var first = compositor.GetWindowOffset(0);
        var second = compositor.GetWindowOffset(1);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo((0, 0)));
            Assert.That(second.X, Is.EqualTo(Resolution.Width / 4));
            Assert.That(second.Y, Is.EqualTo(Resolution.Height / 4));
        });
    }

    [Test(Description = "Границы второго окна отсчитываются от его места в каскаде")]
    public async Task SecondWindowBoundsShouldIncludeCascadeOffset()
    {
        await using var compositor = CreateCompositor();
        using var client = WaylandTestClient.Connect(compositor.SocketPath);

        var first = client.CreateWindow(FirstTitle);
        var second = client.CreateWindow(SecondTitle);
        WaitForWindows(compositor, expected: 2);

        client.SetWindowGeometry(first.XdgSurface, 0, 0, WindowWidth, WindowHeight);
        client.SetWindowGeometry(second.XdgSurface, 0, 0, WindowWidth, WindowHeight);
        WaitForGeometry(compositor, windowIndex: 1);

        var bounds = compositor.GetWindowGeometryByIndex(1);
        var (offsetX, offsetY) = compositor.GetWindowOffset(1);

        Assert.Multiple(() =>
        {
            Assert.That(bounds.X, Is.EqualTo(offsetX));
            Assert.That(bounds.Y, Is.EqualTo(offsetY));
            Assert.That(bounds.Width, Is.EqualTo(WindowWidth));
        });
    }

    [Test(Description = "Указатель входит в то окно, над которым он находится")]
    public async Task PointerShouldEnterWindowUnderCursor()
    {
        await using var compositor = CreateCompositor();
        using var client = WaylandTestClient.Connect(compositor.SocketPath);

        var first = client.CreateWindow(FirstTitle);
        var second = client.CreateWindow(SecondTitle);
        _ = client.CreatePointer();
        WaitForWindows(compositor, expected: 2);

        client.SetWindowGeometry(first.XdgSurface, 0, 0, WindowWidth, WindowHeight);
        client.SetWindowGeometry(second.XdgSurface, 0, 0, WindowWidth, WindowHeight);
        WaitForGeometry(compositor, windowIndex: 1);
        _ = client.DrainEvents();

        // Точка в глубине второго окна: первое до неё не достаёт, его правый край ближе.
        var (offsetX, offsetY) = compositor.GetWindowOffset(1);
        compositor.Input.MoveTo(offsetX + (WindowWidth / 2), offsetY + (WindowHeight / 2));

        var entered = WaitForPointerEnter(client);

        Assert.That(entered, Does.Contain(second.Surface), "Ввод обязан прийти во второе окно.");
        Assert.That(entered, Does.Not.Contain(first.Surface), "Первое окно точку не содержит.");
    }

    [Test(Description = "После закрытия соседа ввод остаётся в своём окне")]
    public async Task PointerShouldFollowWindowIndexAfterNeighbourClosed()
    {
        await using var compositor = CreateCompositor();
        using var client = WaylandTestClient.Connect(compositor.SocketPath);

        var first = client.CreateWindow(FirstTitle);
        var second = client.CreateWindow(SecondTitle);
        _ = client.CreatePointer();
        WaitForWindows(compositor, expected: 2);

        client.SetWindowGeometry(first.XdgSurface, 0, 0, WindowWidth, WindowHeight);
        client.SetWindowGeometry(second.XdgSurface, 0, 0, WindowWidth, WindowHeight);
        WaitForGeometry(compositor, windowIndex: 1);

        // ★ Закрытие первого окна не двигает второе: его номер выдан при создании. Нумерация по
        // месту в обходе сделала бы оставшееся окно нулевым, и клик ушёл бы в пустое место сцены.
        client.DestroyToplevel(first.Toplevel);
        WaitForWindows(compositor, expected: 1);
        _ = client.DrainEvents();

        var (offsetX, offsetY) = compositor.GetWindowOffset(1);
        compositor.Input.MoveTo(offsetX + (WindowWidth / 2), offsetY + (WindowHeight / 2));

        Assert.That(WaitForPointerEnter(client), Does.Contain(second.Surface));
    }

    [Test(Description = "Закрытое окно перестаёт быть целью ввода")]
    public async Task DestroyedWindowShouldStopResolving()
    {
        await using var compositor = CreateCompositor();
        using var client = WaylandTestClient.Connect(compositor.SocketPath);

        client.CreateWindow(FirstTitle);
        var second = client.CreateWindow(SecondTitle);
        WaitForWindows(compositor, expected: 2);

        client.DestroyToplevel(second.Toplevel);
        WaitForWindows(compositor, expected: 1);

        Assert.Multiple(() =>
        {
            Assert.That(compositor.TryResolveWindowIndexByTitle(SecondTitle), Is.Null);
            Assert.That(compositor.TryResolveWindowIndexByTitle(FirstTitle), Is.Zero);
        });
    }

    [Test(Description = "Номер оставшегося окна не сдвигается после закрытия соседнего")]
    public async Task RemainingWindowShouldKeepItsIndex()
    {
        await using var compositor = CreateCompositor();
        using var client = WaylandTestClient.Connect(compositor.SocketPath);

        var first = client.CreateWindow(FirstTitle);
        client.CreateWindow(SecondTitle);
        WaitForWindows(compositor, expected: 2);

        client.DestroyToplevel(first.Toplevel);
        WaitForWindows(compositor, expected: 1);

        // ★ Номер выдан при создании и переживает соседей: сдвиг развёл бы ввод и вывод по разным
        // окнам — клик уходил бы в одно окно, а картинка рисовалась в другом.
        Assert.That(compositor.TryResolveWindowIndexByTitle(SecondTitle), Is.EqualTo(1));
    }

    [Test(Description = "Одинаковые заголовки не дают ложного совпадения")]
    public async Task DuplicateTitlesShouldNotResolve()
    {
        await using var compositor = CreateCompositor();
        using var client = WaylandTestClient.Connect(compositor.SocketPath);

        client.CreateWindow(FirstTitle);
        WaitForWindows(compositor, expected: 1);

        // Второе окно с тем же заголовком делает разрешение неоднозначным.
        client.CreateWindow(FirstTitle);
        WaitFor(
            () => compositor.TryResolveWindowIndexByTitle(FirstTitle) is null,
            "Дубль заголовка обязан сделать разрешение неоднозначным.");

        // Различить их нечем — пусть решает счётчик драйвера, а не случайный порядок обхода.
        Assert.That(compositor.TryResolveWindowIndexByTitle(FirstTitle), Is.Null);
    }

    [Test(Description = "Неизвестный заголовок не разрешается в номер окна")]
    public async Task UnknownTitleShouldNotResolve()
    {
        await using var compositor = CreateCompositor();
        using var client = WaylandTestClient.Connect(compositor.SocketPath);

        client.CreateWindow(FirstTitle);
        WaitForWindows(compositor, expected: 1);

        Assert.Multiple(() =>
        {
            Assert.That(compositor.TryResolveWindowIndexByTitle("Нет такого окна"), Is.Null);
            Assert.That(compositor.TryResolveWindowIndexByTitle(null), Is.Null);
        });
    }

    [Test(Description = "Цикл событий переживает появление второго окна")]
    public async Task EventLoopShouldSurviveSecondWindow()
    {
        await using var compositor = CreateCompositor();
        using var client = WaylandTestClient.Connect(compositor.SocketPath);

        client.CreateWindow(FirstTitle);
        WaitForWindows(compositor, expected: 1);

        client.CreateWindow(SecondTitle);
        WaitForWindows(compositor, expected: 2);

        // ★ Живость проверяется новым запросом: сорванный цикл событий перестал бы обслуживать
        // клиента, и третье окно не появилось бы вовсе.
        client.CreateWindow(ThirdTitle);
        WaitForWindows(compositor, expected: 3);

        Assert.That(compositor.TryResolveWindowIndexByTitle(ThirdTitle), Is.EqualTo(2));
    }

    [Test(Description = "Указатель покидает окно, из которого ушёл курсор")]
    public async Task PointerShouldLeaveWindowItMovedAwayFrom()
    {
        await using var compositor = CreateCompositor();
        using var client = WaylandTestClient.Connect(compositor.SocketPath);

        var first = client.CreateWindow(FirstTitle);
        var second = client.CreateWindow(SecondTitle);
        _ = client.CreatePointer();
        WaitForWindows(compositor, expected: 2);

        client.SetWindowGeometry(first.XdgSurface, 0, 0, WindowWidth, WindowHeight);
        client.SetWindowGeometry(second.XdgSurface, 0, 0, WindowWidth, WindowHeight);
        WaitForGeometry(compositor, windowIndex: 1);

        // Точка внутри первого окна и вне второго: его левый край дальше по каскаду.
        compositor.Input.MoveTo(NearCorner, NearCorner);
        _ = WaitForPointerEnter(client);

        var (offsetX, offsetY) = compositor.GetWindowOffset(1);
        compositor.Input.MoveTo(offsetX + (WindowWidth / 2), offsetY + (WindowHeight / 2));

        // ★ Без leave первое окно навсегда держит подсветку под курсором, а начатое в нём
        // перетаскивание не отпускается при переходе в соседнее окно.
        var left = new List<uint>();

        WaitFor(
            () =>
            {
                foreach (var (objectId, opcode, body) in client.DrainEvents())
                {
                    if (objectId == client.Pointer && opcode == PointerLeave)
                        left.Add(WaylandTestClient.ReadLeaveSurface(body));
                }

                return left.Contains(first.Surface);
            },
            "Указатель не покинул первое окно.");

        Assert.That(left, Does.Contain(first.Surface));
    }

    [Test(Description = "Нажатие уходит только окну с клавиатурным фокусом")]
    public async Task KeyShouldReachOnlyFocusedWindow()
    {
        await using var compositor = CreateCompositor();
        using var client = WaylandTestClient.Connect(compositor.SocketPath);

        client.CreateWindow(FirstTitle);
        client.CreateWindow(SecondTitle);
        _ = client.CreateKeyboard();
        WaitForWindows(compositor, expected: 2);

        compositor.Input.SetKeyboardFocus(hasFocus: true, windowIndex: 1);
        Thread.Sleep(FocusSettleDelay);
        _ = client.DrainEvents();

        compositor.Input.SendKey(KeyCodeA, pressed: true);
        compositor.Input.SendKey(KeyCodeA, pressed: false);
        Thread.Sleep(FocusSettleDelay);

        // ★ Фокус клавиатуры один на сеанс: нажатие обязано прийти ровно один раз. Раздача фокуса
        // всем окнам удваивала каждое нажатие — страница получала два символа вместо одного.
        var keyEvents = 0;

        foreach (var (objectId, opcode, _) in client.DrainEvents())
        {
            if (objectId == client.Keyboard && opcode == KeyboardKey)
                ++keyEvents;
        }

        Assert.That(keyEvents, Is.EqualTo(2), "Ожидались ровно нажатие и отпускание одного окна.");
    }

    [Test(Description = "Запрос закрытого окна не валит композитор")]
    public async Task CommandFromDestroyedWindowShouldBeIgnored()
    {
        await using var compositor = CreateCompositor();
        using var client = WaylandTestClient.Connect(compositor.SocketPath);

        client.CreateWindow(FirstTitle);
        var second = client.CreateWindow(SecondTitle);
        WaitForWindows(compositor, expected: 2);

        client.DestroyToplevel(second.Toplevel);
        WaitForWindows(compositor, expected: 1);

        // ★ Клиент шлёт запрос уже уничтоженному объекту: сообщение было в пути, когда окно
        // закрылось. Композитор обязан его отбросить, а не сорвать цикл событий.
        client.MinimizeToplevel(second.Toplevel);
        client.CreateWindow(ThirdTitle);

        WaitForWindows(compositor, expected: 2);

        Assert.That(compositor.TryResolveWindowIndexByTitle(ThirdTitle), Is.EqualTo(2));
    }

    [Test(Description = "Композитор переживает закрытие и повторное открытие окна")]
    public async Task ReopenedWindowShouldResolveAgain()
    {
        await using var compositor = CreateCompositor();
        using var client = WaylandTestClient.Connect(compositor.SocketPath);

        client.CreateWindow(FirstTitle);
        var second = client.CreateWindow(SecondTitle);
        WaitForWindows(compositor, expected: 2);

        client.DestroyToplevel(second.Toplevel);
        WaitForWindows(compositor, expected: 1);

        // ★ Клиент переиспользует освободившиеся номера объектов: без забвения состояния ввода
        // новая поверхность считалась бы уже принятой, и указатель в ней молчал бы.
        client.CreateWindow(ThirdTitle);
        WaitForWindows(compositor, expected: 2);

        Assert.That(compositor.TryResolveWindowIndexByTitle(ThirdTitle), Is.EqualTo(2));
    }

    [Test(Description = "Номера закрытых окон не заставляют открывать лишние")]
    public async Task ClosedWindowNumbersShouldNotSpawnExtraWindows()
    {
        await using var compositor = CreateCompositor();
        using var client = WaylandTestClient.Connect(compositor.SocketPath);

        client.CreateWindow(FirstTitle);
        var second = client.CreateWindow(SecondTitle);
        var third = client.CreateWindow(ThirdTitle);
        WaitForWindows(compositor, expected: 3);

        // ★ Закрываем оба хвостовых окна: номера 1 и 2 выбывают навсегда, счётчик их не переиспользует.
        client.DestroyToplevel(second.Toplevel);
        client.DestroyToplevel(third.Toplevel);
        WaitForWindows(compositor, expected: 1);

        // Следующее окно получит номер 3. Привязка окон вывода к ПОЗИЦИИ заставляла открывать ещё
        // и окна под пропущенные номера 1 и 2 — каждое с собственным буфером кадра.
        client.CreateWindow(SecondTitle);
        WaitForWindows(compositor, expected: 2);

        // Число хост-окон здесь не проверить: вывод выключен, и окна не открываются вовсе.
        Assert.Multiple(() =>
        {
            Assert.That(compositor.TryResolveWindowIndexByTitle(SecondTitle), Is.EqualTo(3));
            Assert.That(compositor.TryResolveWindowIndexByTitle(FirstTitle), Is.Zero);
        });
    }

    private static WaylandCompositor CreateCompositor()
        => WaylandCompositor.Create(new WaylandCompositorSettings
        {
            Resolution = Resolution,
            EnablePresentation = false,
        });

    private static void WaitForWindows(WaylandCompositor compositor, int expected)
        => WaitFor(() => CountWindows(compositor) == expected, "Окон так и не стало " + expected + ".");

    private static void WaitForGeometry(WaylandCompositor compositor, int windowIndex)
        => WaitFor(
            () => compositor.GetWindowGeometryByIndex(windowIndex).Width == WindowWidth,
            "Композитор не принял размер окна.");

    private static int CountWindows(WaylandCompositor compositor)
    {
        var count = 0;

        foreach (var title in new[] { FirstTitle, SecondTitle, ThirdTitle })
        {
            if (compositor.TryResolveWindowIndexByTitle(title) is not null)
                ++count;
        }

        return count;
    }

    private static List<uint> WaitForPointerEnter(WaylandTestClient client)
    {
        var entered = new List<uint>();

        WaitFor(
            () =>
            {
                foreach (var (objectId, opcode, body) in client.DrainEvents())
                {
                    if (objectId == client.Pointer && opcode == PointerEnter)
                        entered.Add(WaylandTestClient.ReadEnterSurface(body));
                }

                return entered.Count > 0;
            },
            "Указатель не вошёл ни в одно окно.");

        return entered;
    }

    private static void WaitFor(Func<bool> condition, string message)
    {
        for (var attempt = 0; attempt < WaitAttempts; ++attempt)
        {
            if (condition())
                return;

            Thread.Sleep(PollDelay);
        }

        Assert.Fail(message);
    }

    private const string FirstTitle = "Окно первое";
    private const string SecondTitle = "Окно второе";
    private const string ThirdTitle = "Окно третье";
    private const int WindowWidth = 800;
    private const int WindowHeight = 600;
    private const int WaitAttempts = 100;
    private const int PollDelay = 20;
    private const ushort PointerEnter = 0;
    private const ushort PointerLeave = 1;
    private const ushort KeyboardKey = 3;
    private const uint KeyCodeA = 30;
    private const int NearCorner = 40;
    private const int FocusSettleDelay = 120;

    private static readonly Size Resolution = new(1920, 1080);
}
