using System.Drawing;
using Microsoft.Extensions.Logging;

namespace Atom.Hardware.Display;

/// <summary>
/// Настройки виртуального дисплея.
/// </summary>
public sealed record VirtualDisplaySettings
{
    /// <summary>
    /// Логгер для operational diagnostics виртуального дисплея.
    /// Некритичные сообщения xpra и attach направляются сюда.
    /// Критические ошибки инициализации по-прежнему выбрасываются как исключения.
    /// </summary>
    public ILogger? Logger { get; init; }

    /// <summary>
    /// Разрешение виртуального экрана в пикселях.
    /// </summary>
    public Size Resolution { get; init; } = new(1920, 1080);

    /// <summary>
    /// Глубина цвета в битах на пиксель.
    /// </summary>
    public int ColorDepth { get; init; } = 24;

    /// <summary>
    /// Номер дисплея X11 (например, 99 → <c lang="text">:99</c>).
    /// Если <see langword="null"/>, номер выбирается автоматически.
    /// </summary>
    public int? DisplayNumber { get; init; }

    /// <summary>
    /// Отображать содержимое дисплея в окне на реальном экране.
    /// Когда <see langword="true"/>, для xpra-сессии запускается локальный rootless attach,
    /// и окна приложений публикуются на хостовом рабочем столе как отдельные окна.
    /// Когда <see langword="false"/> (по умолчанию), xpra-сессия остаётся неаттаченной и невидимой.
    /// </summary>
    public bool IsVisible { get; init; }

    /// <summary>
    /// Заявлять сенсорный ввод.
    /// </summary>
    /// <remarks>
    /// Наблюдаемо страницей через <c lang="text">navigator.maxTouchPoints</c>, поэтому должно
    /// соответствовать личности, под которую маскируется вкладка.
    /// </remarks>
    public bool HasTouch { get; init; }

    /// <summary>
    /// Путь к приложению, которое будет запущено на дисплее.
    /// </summary>
    /// <remarks>
    /// Определяет заголовок и иконку окна на панели задач при видимом дисплее.
    /// </remarks>
    public string? ApplicationPath { get; init; }

    /// <summary>
    /// Поднимать дисплей на X11 (Xvfb или xpra) вместо собственного композитора.
    /// </summary>
    /// <remarks>
    /// ★ Старый путь: требует внешних пакетов и держит отдельные процессы. Оставлен для
    /// совместимости и случаев, где нужен именно X-сервер.
    /// </remarks>
    public bool UseX11Backend { get; init; }
}
