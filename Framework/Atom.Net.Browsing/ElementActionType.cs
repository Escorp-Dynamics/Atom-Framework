namespace Atom.Net.Browsing;

/// <summary>
/// Действие над элементом.
/// </summary>
public enum ElementActionType
{
    /// <summary>
    /// Клик по элементу.
    /// </summary>
    Click,

    /// <summary>
    /// Двойной клик.
    /// </summary>
    DoubleClick,

    /// <summary>
    /// Ввод текста.
    /// </summary>
    Type,

    /// <summary>
    /// Очистка поля.
    /// </summary>
    Clear,

    /// <summary>
    /// Наведение курсора.
    /// </summary>
    Hover,

    /// <summary>
    /// Установка фокуса.
    /// </summary>
    Focus,

    /// <summary>
    /// Прокрутка к элементу.
    /// </summary>
    ScrollIntoView,

    /// <summary>
    /// Выбор значения (для select).
    /// </summary>
    Select,

    /// <summary>
    /// Установка/снятие флажка (для checkbox).
    /// </summary>
    Check,
}
