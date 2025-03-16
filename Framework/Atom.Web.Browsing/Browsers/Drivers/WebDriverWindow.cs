using Atom.Web.Browsing.BiDi;

namespace Atom.Web.Browsing.Drivers;

/// <summary>
/// Представляет окно драйвера веб-браузера.
/// </summary>
public class WebDriverWindow : WebWindow, IWebDriverWindow
{
    /// <inheritdoc/>
    public BiDiDriver BiDi { get; protected set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebDriverWindow"/>.
    /// </summary>
    /// <param name="context">Контекст драйвера.</param>
    /// <param name="settings">Настройки контекста.</param>
    /// <param name="biDi">Ссылка на соединение с BiDi.</param>
    protected internal WebDriverWindow(IWebDriverContext context, IWebDriverWindowSettings settings, BiDiDriver biDi) : base(context, settings) => BiDi = biDi;
}