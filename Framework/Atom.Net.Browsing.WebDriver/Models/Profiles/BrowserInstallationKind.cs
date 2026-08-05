namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Определяет способ установки браузера в системе.
/// </summary>
public enum BrowserInstallationKind
{
    /// <summary>
    /// Нативная установка ОС (deb/rpm-пакеты, MSI-инсталляторы, .app bundle и т.п.).
    /// </summary>
    Native,

    /// <summary>
    /// Установка через Flatpak (бинарный файл разрешён через exports-обёртку app id).
    /// </summary>
    Flatpak,

    /// <summary>
    /// Установка через Snap (бинарный файл разрешён через каталог launcher-скриптов snap).
    /// </summary>
    Snap,
}
