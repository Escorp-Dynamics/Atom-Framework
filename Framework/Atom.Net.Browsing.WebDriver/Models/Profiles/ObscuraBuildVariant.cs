namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Вариант официальной сборки Obscura.
/// </summary>
public enum ObscuraBuildVariant
{
    /// <summary>
    /// Сборка с рендерингом (скриншоты, PDF, screencast) без stealth-транспорта.
    /// </summary>
    Default,

    /// <summary>
    /// Сборка с рендерингом и stealth-транспортом (TLS-имперсонация, блокировка трекеров).
    /// </summary>
    Stealth,

    /// <summary>
    /// Облегчённая сборка без рендеринга и без stealth-транспорта.
    /// </summary>
    NoRender,

    /// <summary>
    /// Облегчённая сборка без рендеринга со stealth-транспортом.
    /// </summary>
    NoRenderStealth,
}
