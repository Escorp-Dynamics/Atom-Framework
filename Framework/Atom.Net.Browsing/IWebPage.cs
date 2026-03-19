using System.Text.Json;

namespace Atom.Net.Browsing;

/// <summary>
/// Представляет вкладку браузера.
/// </summary>
public interface IWebPage : IDomContext, IAsyncDisposable
{
    /// <summary>
    /// Главный фрейм страницы.
    /// Все DOM-операции (поиск элементов, выполнение скриптов и т.д.)
    /// по умолчанию делегируются через этот фрейм.
    /// </summary>
    IFrame MainFrame { get; }

    /// <summary>
    /// Выполняет навигацию по указанному адресу.
    /// </summary>
    /// <param name="url">Адрес страницы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask NavigateAsync(Uri url, CancellationToken cancellationToken);

    /// <inheritdoc cref="NavigateAsync(Uri, CancellationToken)"/>
    ValueTask NavigateAsync(Uri url) => NavigateAsync(url, CancellationToken.None);

    /// <summary>
    /// Делает снимок экрана вкладки.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Снимок в формате PNG, закодированный в Base64.</returns>
    ValueTask<string?> CaptureScreenshotAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="CaptureScreenshotAsync(CancellationToken)"/>
    ValueTask<string?> CaptureScreenshotAsync() => CaptureScreenshotAsync(CancellationToken.None);

    /// <summary>
    /// Описывает доступные backend'ы ввода для страницы.
    /// </summary>
    PageInputCapabilities InputCapabilities { get; }

    /// <summary>
    /// Выполняет point click по точке viewport страницы.
    /// Координаты задаются в CSS-пикселях относительно клиентской области документа.
    /// Backend выбирается на основании <paramref name="options"/> и <see cref="InputCapabilities"/>.
    /// </summary>
    /// <param name="viewportX">Координата X внутри viewport.</param>
    /// <param name="viewportY">Координата Y внутри viewport.</param>
    /// <param name="options">Параметры выбора backend.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask ClickPointAsync(double viewportX, double viewportY, PagePointClickOptions options, CancellationToken cancellationToken);

    /// <inheritdoc cref="ClickPointAsync(double, double, PagePointClickOptions, CancellationToken)"/>
    ValueTask ClickPointAsync(double viewportX, double viewportY, CancellationToken cancellationToken)
        => ClickPointAsync(viewportX, viewportY, PagePointClickOptions.Default, cancellationToken);

    /// <inheritdoc cref="ClickPointAsync(double, double, PagePointClickOptions, CancellationToken)"/>
    ValueTask ClickPointAsync(double viewportX, double viewportY, PagePointClickOptions options)
        => ClickPointAsync(viewportX, viewportY, options, CancellationToken.None);

    /// <inheritdoc cref="ClickPointAsync(double, double, PagePointClickOptions, CancellationToken)"/>
    ValueTask ClickPointAsync(double viewportX, double viewportY)
        => ClickPointAsync(viewportX, viewportY, PagePointClickOptions.Default, CancellationToken.None);

    /// <summary>
    /// Выполняет click по элементу, найденному селектором в главном DOM-контексте страницы.
    /// Для parallel-safe backend используется page-local dispatch на самом элементе.
    /// </summary>
    /// <param name="selector">Селектор элемента.</param>
    /// <param name="options">Параметры выбора backend.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask ClickElementAsync(ElementSelector selector, PageElementClickOptions options, CancellationToken cancellationToken);

    /// <inheritdoc cref="ClickElementAsync(ElementSelector, PageElementClickOptions, CancellationToken)"/>
    ValueTask ClickElementAsync(ElementSelector selector, CancellationToken cancellationToken)
        => ClickElementAsync(selector, PageElementClickOptions.Default, cancellationToken);

    /// <inheritdoc cref="ClickElementAsync(ElementSelector, PageElementClickOptions, CancellationToken)"/>
    ValueTask ClickElementAsync(ElementSelector selector, PageElementClickOptions options)
        => ClickElementAsync(selector, options, CancellationToken.None);

    /// <inheritdoc cref="ClickElementAsync(ElementSelector, PageElementClickOptions, CancellationToken)"/>
    ValueTask ClickElementAsync(ElementSelector selector)
        => ClickElementAsync(selector, PageElementClickOptions.Default, CancellationToken.None);

    /// <summary>
    /// Устанавливает фокус на элемент страницы, найденный селектором.
    /// Операция выполняется tab-local backend'ом и совместима с headless/parallel сценариями.
    /// </summary>
    /// <param name="selector">Селектор элемента.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask FocusElementAsync(ElementSelector selector, CancellationToken cancellationToken);

    /// <inheritdoc cref="FocusElementAsync(ElementSelector, CancellationToken)"/>
    ValueTask FocusElementAsync(ElementSelector selector)
        => FocusElementAsync(selector, CancellationToken.None);

    /// <summary>
    /// Наводит pointer на элемент страницы, найденный селектором.
    /// Операция выполняется tab-local backend'ом и совместима с headless/parallel сценариями.
    /// </summary>
    /// <param name="selector">Селектор элемента.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask HoverElementAsync(ElementSelector selector, CancellationToken cancellationToken);

    /// <inheritdoc cref="HoverElementAsync(ElementSelector, CancellationToken)"/>
    ValueTask HoverElementAsync(ElementSelector selector)
        => HoverElementAsync(selector, CancellationToken.None);

    /// <summary>
    /// Вводит текст в элемент страницы, найденный селектором.
    /// Операция выполняется tab-local backend'ом и рассчитана на headless/parallel сценарии.
    /// </summary>
    /// <param name="selector">Селектор элемента.</param>
    /// <param name="text">Текст для ввода.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask TypeElementAsync(ElementSelector selector, string text, CancellationToken cancellationToken);

    /// <inheritdoc cref="TypeElementAsync(ElementSelector, string, CancellationToken)"/>
    ValueTask TypeElementAsync(ElementSelector selector, string text)
        => TypeElementAsync(selector, text, CancellationToken.None);

    /// <summary>
    /// Переводит checkbox/radio-элемент в checked state.
    /// Если элемент уже отмечен, операция является идемпотентной.
    /// </summary>
    /// <param name="selector">Селектор элемента.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask CheckElementAsync(ElementSelector selector, CancellationToken cancellationToken);

    /// <inheritdoc cref="CheckElementAsync(ElementSelector, CancellationToken)"/>
    ValueTask CheckElementAsync(ElementSelector selector)
        => CheckElementAsync(selector, CancellationToken.None);

    /// <summary>
    /// Отправляет keyboard key press в контекст страницы.
    /// Backend выбирается на основании <paramref name="options"/> и <see cref="InputCapabilities"/>.
    /// </summary>
    /// <param name="key">Имя клавиши. Минимально поддерживаются Space и Enter.</param>
    /// <param name="options">Параметры выбора backend.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask KeyPressAsync(string key, PageKeyPressOptions options, CancellationToken cancellationToken);

    /// <inheritdoc cref="KeyPressAsync(string, PageKeyPressOptions, CancellationToken)"/>
    ValueTask KeyPressAsync(string key, CancellationToken cancellationToken)
        => KeyPressAsync(key, PageKeyPressOptions.Default, cancellationToken);

    /// <inheritdoc cref="KeyPressAsync(string, PageKeyPressOptions, CancellationToken)"/>
    ValueTask KeyPressAsync(string key, PageKeyPressOptions options)
        => KeyPressAsync(key, options, CancellationToken.None);

    /// <inheritdoc cref="KeyPressAsync(string, PageKeyPressOptions, CancellationToken)"/>
    ValueTask KeyPressAsync(string key)
        => KeyPressAsync(key, PageKeyPressOptions.Default, CancellationToken.None);

    /// <summary>
    /// Ожидает завершения навигации.
    /// </summary>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask WaitForNavigationAsync(TimeSpan? timeout, CancellationToken cancellationToken);

    /// <inheritdoc cref="WaitForNavigationAsync(TimeSpan?, CancellationToken)"/>
    ValueTask WaitForNavigationAsync(TimeSpan? timeout)
        => WaitForNavigationAsync(timeout, CancellationToken.None);

    /// <inheritdoc cref="WaitForNavigationAsync(TimeSpan?, CancellationToken)"/>
    ValueTask WaitForNavigationAsync(CancellationToken cancellationToken)
        => WaitForNavigationAsync(timeout: null, cancellationToken);

    /// <inheritdoc cref="WaitForNavigationAsync(TimeSpan?, CancellationToken)"/>
    ValueTask WaitForNavigationAsync()
        => WaitForNavigationAsync(timeout: null, CancellationToken.None);

    /// <summary>
    /// Устанавливает cookie.
    /// </summary>
    /// <param name="name">Имя cookie.</param>
    /// <param name="value">Значение cookie.</param>
    /// <param name="domain">Домен.</param>
    /// <param name="path">Путь.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask SetCookieAsync(string name, string value, string? domain, string? path, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetCookieAsync(string, string, string?, string?, CancellationToken)"/>
    ValueTask SetCookieAsync(string name, string value, string? domain, string? path)
        => SetCookieAsync(name, value, domain, path, CancellationToken.None);

    /// <inheritdoc cref="SetCookieAsync(string, string, string?, string?, CancellationToken)"/>
    ValueTask SetCookieAsync(string name, string value, CancellationToken cancellationToken)
        => SetCookieAsync(name, value, domain: null, path: null, cancellationToken);

    /// <inheritdoc cref="SetCookieAsync(string, string, string?, string?, CancellationToken)"/>
    ValueTask SetCookieAsync(string name, string value)
        => SetCookieAsync(name, value, domain: null, path: null, CancellationToken.None);

    /// <summary>
    /// Получает все cookies текущей страницы.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask<JsonElement?> GetCookiesAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetCookiesAsync(CancellationToken)"/>
    ValueTask<JsonElement?> GetCookiesAsync() => GetCookiesAsync(CancellationToken.None);

    /// <summary>
    /// Удаляет все cookies.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    ValueTask DeleteCookiesAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="DeleteCookiesAsync(CancellationToken)"/>
    ValueTask DeleteCookiesAsync() => DeleteCookiesAsync(CancellationToken.None);

    /// <summary>
    /// Происходит при получении консольного сообщения от вкладки.
    /// </summary>
#pragma warning disable CA1003
    event AsyncEventHandler<IWebPage, ConsoleMessageEventArgs>? ConsoleMessage;
#pragma warning restore CA1003
}