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
    /// Номер дисплея X11 (например, 99 → <c>:99</c>).
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
}
