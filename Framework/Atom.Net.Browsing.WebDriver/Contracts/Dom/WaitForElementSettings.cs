namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Описывает параметры ожидания DOM-элемента.
/// </summary>
public sealed class WaitForElementSettings
{
    /// <summary>
    /// Получает или задает селектор ожидаемого элемента.
    /// </summary>
    public required ElementSelector Selector { get; init; }

    /// <summary>
    /// Получает или задает максимальное время ожидания.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Получает или задает режим ожидания элемента.
    /// </summary>
    public WaitForElementKind Kind { get; init; } = WaitForElementKind.Attached;
}