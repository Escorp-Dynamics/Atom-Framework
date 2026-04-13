namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Определяет режимы ожидания элемента на странице.
/// </summary>
[Flags]
public enum WaitForElementKind
{
    /// <summary>
    /// Ожидать появления элемента в DOM.
    /// </summary>
    Attached = 1,

    /// <summary>
    /// Ожидать, что элемент станет видимым.
    /// </summary>
    Visible = 2,

    /// <summary>
    /// Ожидать, что элемент стабилизируется по положению и размеру.
    /// </summary>
    Stable = 4,
}