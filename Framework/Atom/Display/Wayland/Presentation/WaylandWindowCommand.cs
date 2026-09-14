using System.Runtime.InteropServices;

namespace Atom.Display.Wayland.Presentation;

/// <summary>
/// Запрос браузера к оболочке окна.
/// </summary>
/// <remarks>
/// ★ Браузер рисует заголовок и кнопки сам, а действия за ними выполняет оболочка. Мы для браузера
/// и есть оболочка, поэтому запрос надо переадресовать настоящей — в сессию разработчика. Иначе
/// кнопки нарисованы, но не работают: перетаскивание не двигает окно, свернуть ничего не делает.
/// </remarks>
internal readonly record struct WaylandWindowCommand
{
    /// <summary>Что требуется сделать с окном.</summary>
    public required WaylandWindowCommandKind Kind { get; init; }

    /// <summary>Текстовый параметр: заголовок окна или идентификатор приложения.</summary>
    public string? Text { get; init; }

    /// <summary>Развернуть, а не восстановить.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>Край, за который тянут окно.</summary>
    public uint Edge { get; init; }

    /// <summary>Координата по горизонтали.</summary>
    public int X { get; init; }

    /// <summary>Координата по вертикали.</summary>
    public int Y { get; init; }

    /// <summary>Порядковый номер нажатия, на которое ссылается клиент.</summary>
    public uint Serial { get; init; }

    /// <summary>Запросу нужен живой жест в сессии хоста.</summary>
    public bool RequiresGestureSerial
        => Kind is WaylandWindowCommandKind.Move or WaylandWindowCommandKind.Resize or WaylandWindowCommandKind.ShowMenu;

    /// <summary>Смена заголовка.</summary>
    public static WaylandWindowCommand SetTitle(string title)
        => new() { Kind = WaylandWindowCommandKind.SetTitle, Text = title };

    /// <summary>Смена идентификатора приложения.</summary>
    public static WaylandWindowCommand SetAppId(string appId)
        => new() { Kind = WaylandWindowCommandKind.SetAppId, Text = appId };

    /// <summary>Перетаскивание окна.</summary>
    public static WaylandWindowCommand Move(uint serial)
        => new() { Kind = WaylandWindowCommandKind.Move, Serial = serial };

    /// <summary>Изменение размера за край.</summary>
    public static WaylandWindowCommand Resize(uint serial, uint edge)
        => new() { Kind = WaylandWindowCommandKind.Resize, Serial = serial, Edge = edge };

    /// <summary>Разворот или восстановление.</summary>
    public static WaylandWindowCommand Maximize(bool isEnabled)
        => new() { Kind = WaylandWindowCommandKind.Maximize, IsEnabled = isEnabled };

    /// <summary>Разворот во весь экран.</summary>
    public static WaylandWindowCommand Fullscreen(bool isEnabled)
        => new() { Kind = WaylandWindowCommandKind.Fullscreen, IsEnabled = isEnabled };

    /// <summary>Сворачивание.</summary>
    public static WaylandWindowCommand Minimize() => new() { Kind = WaylandWindowCommandKind.Minimize };

    /// <summary>Системное меню окна.</summary>
    public static WaylandWindowCommand ShowMenu(uint serial, int x, int y)
        => new() { Kind = WaylandWindowCommandKind.ShowMenu, Serial = serial, X = x, Y = y };
}

/// <summary>Вид запроса к оболочке окна.</summary>
internal enum WaylandWindowCommandKind
{
    /// <summary>Смена заголовка.</summary>
    SetTitle,

    /// <summary>Смена идентификатора приложения.</summary>
    SetAppId,

    /// <summary>Перетаскивание.</summary>
    Move,

    /// <summary>Изменение размера.</summary>
    Resize,

    /// <summary>Разворот или восстановление.</summary>
    Maximize,

    /// <summary>Разворот во весь экран.</summary>
    Fullscreen,

    /// <summary>Сворачивание.</summary>
    Minimize,

    /// <summary>Системное меню.</summary>
    ShowMenu,
}

/// <summary>
/// Границы окна внутри поверхности клиента.
/// </summary>
/// <param name="X">Отступ слева.</param>
/// <param name="Y">Отступ сверху.</param>
/// <param name="Width">Ширина окна.</param>
/// <param name="Height">Высота окна.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct WaylandWindowGeometry(int X, int Y, int Width, int Height);
