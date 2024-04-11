namespace Atom.Web.Browsers;

/// <summary>
/// Представляет настройки страницы в браузере.
/// </summary>
public class PageSettings
{
    /// <summary>
    /// Адрес страницы.
    /// </summary>
    public Uri Url { get; set; }

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="PageSettings"/>.
    /// </summary>
    public PageSettings()
    {
        Url = new Uri("about:blank");
    }
}