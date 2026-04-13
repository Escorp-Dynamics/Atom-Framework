using Atom.Hardware.Input;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Описывает параметры клика по DOM-элементу.
/// </summary>
public class ClickSettings
{
    /// <summary>
    /// Получает или задает кнопку мыши, используемую при клике.
    /// </summary>
    public VirtualMouseButton Button { get; init; } = VirtualMouseButton.Left;

    /// <summary>
    /// Получает или задает количество последовательных кликов.
    /// </summary>
    public int ClickCount { get; init; } = 1;

    /// <summary>
    /// Получает или задает задержку между действиями клика.
    /// </summary>
    public TimeSpan? Delay { get; init; }
}