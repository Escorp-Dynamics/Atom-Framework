using System.Drawing;

namespace Atom.Hardware.Input.Backends;

/// <summary>
/// Платформенный бэкенд виртуальной мыши.
/// Отвечает за создание виртуального устройства ввода
/// и генерацию событий мыши на уровне ОС.
/// </summary>
internal interface IVirtualMouseBackend : IAsyncDisposable
{
    /// <summary>
    /// Идентификатор виртуального устройства в системе.
    /// </summary>
    string DeviceIdentifier { get; }

    /// <summary>
    /// Указывает, имеет ли виртуальная мышь отдельный курсор (MPX).
    /// </summary>
    bool HasSeparateCursor { get; }

    /// <summary>
    /// Инициализирует виртуальное устройство мыши.
    /// </summary>
    /// <param name="settings">Настройки мыши.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask InitializeAsync(VirtualMouseSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Перемещает курсор в абсолютные координаты.
    /// </summary>
    /// <param name="position">Позиция на экране.</param>
    void MoveAbsolute(Point position);

    /// <summary>
    /// Перемещает курсор относительно текущей позиции.
    /// </summary>
    /// <param name="delta">Смещение.</param>
    void MoveRelative(Size delta);

    /// <summary>
    /// Нажимает кнопку мыши (без отпускания).
    /// </summary>
    /// <param name="button">Кнопка.</param>
    void ButtonDown(VirtualMouseButton button);

    /// <summary>
    /// Отпускает кнопку мыши.
    /// </summary>
    /// <param name="button">Кнопка.</param>
    void ButtonUp(VirtualMouseButton button);

    /// <summary>
    /// Прокрутка вертикального колеса.
    /// </summary>
    /// <param name="delta">Дельта прокрутки (положительное — вверх).</param>
    void Scroll(int delta);

    /// <summary>
    /// Прокрутка горизонтального колеса.
    /// </summary>
    /// <param name="delta">Дельта прокрутки (положительное — вправо).</param>
    void ScrollHorizontal(int delta);
}
