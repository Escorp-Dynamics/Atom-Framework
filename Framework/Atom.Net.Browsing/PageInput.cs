using System.Runtime.InteropServices;

namespace Atom.Net.Browsing;

/// <summary>
/// Предпочтение при выборе backend для ввода.
/// </summary>
public enum PageInputPreference
{
    /// <summary>
    /// Использовать backend по умолчанию, безопасный для headless/parallel сценариев.
    /// </summary>
    Default,

    /// <summary>
    /// Предпочесть parallel-safe backend вкладки.
    /// </summary>
    PreferParallel,
}

/// <summary>
/// Описывает возможности backend'ов ввода страницы.
/// </summary>
/// <param name="SupportsParallelPointClick">Доступен ли tab-local point input, совместимый с headless и параллельными табами.</param>
/// <param name="SupportsParallelKeyPress">Доступен ли tab-local keyboard input, совместимый с headless и параллельными табами.</param>
/// <param name="SupportsTrustedPointClick">Доступен ли trusted point input через нативный ввод ОС.</param>
/// <param name="SupportsTrustedKeyPress">Доступен ли trusted keyboard input через нативный ввод ОС.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct PageInputCapabilities(
    bool SupportsParallelPointClick,
    bool SupportsParallelKeyPress,
    bool SupportsTrustedPointClick = false,
    bool SupportsTrustedKeyPress = false);

/// <summary>
/// Параметры point input по координатам viewport.
/// </summary>
/// <param name="Preference">Предпочтение выбора backend.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct PagePointClickOptions(PageInputPreference Preference)
{
    /// <summary>
    /// Параметры по умолчанию.
    /// </summary>
    public static PagePointClickOptions Default => new(PageInputPreference.Default);

    /// <summary>
    /// Явно предпочесть parallel-safe backend вкладки.
    /// </summary>
    public static PagePointClickOptions PreferParallel => new(PageInputPreference.PreferParallel);
}

/// <summary>
/// Параметры element-oriented click.
/// </summary>
/// <param name="Preference">Предпочтение выбора backend.</param>
/// <param name="ScrollIntoView">Прокручивать ли элемент в viewport перед взаимодействием.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct PageElementClickOptions(PageInputPreference Preference, bool ScrollIntoView)
{
    /// <summary>
    /// Параметры по умолчанию.
    /// </summary>
    public static PageElementClickOptions Default => new(PageInputPreference.Default, ScrollIntoView: true);

    /// <summary>
    /// Явно предпочесть parallel-safe backend вкладки.
    /// </summary>
    public static PageElementClickOptions PreferParallel => new(PageInputPreference.PreferParallel, ScrollIntoView: true);
}

/// <summary>
/// Параметры keyboard input.
/// </summary>
/// <param name="Preference">Предпочтение выбора backend.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct PageKeyPressOptions(PageInputPreference Preference)
{
    /// <summary>
    /// Параметры по умолчанию.
    /// </summary>
    public static PageKeyPressOptions Default => new(PageInputPreference.Default);

    /// <summary>
    /// Явно предпочесть parallel-safe backend вкладки.
    /// </summary>
    public static PageKeyPressOptions PreferParallel => new(PageInputPreference.PreferParallel);
}