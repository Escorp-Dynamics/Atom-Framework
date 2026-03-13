using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет вкладку браузера, управляемую через WebSocket-мост.
/// </summary>
/// <remarks>
/// Каждая вкладка имеет собственный изолированный канал связи с расширением,
/// что обеспечивает независимость контекстов исполнения.
/// </remarks>
public sealed class WebDriverPage : IWebPage
{
    private readonly TabChannel channel;
    private bool isDisposed;

    /// <summary>
    /// Идентификатор вкладки.
    /// </summary>
    public string TabId => channel.TabId;

    /// <summary>
    /// Определяет, подключена ли вкладка к мосту.
    /// </summary>
    public bool IsConnected => channel.IsConnected;

    /// <inheritdoc />
    public IFrame MainFrame { get; }

    /// <inheritdoc />
    public IEnumerable<IFrame> Frames => MainFrame.Frames;

    /// <summary>
    /// Происходит при получении события от вкладки.
    /// </summary>
    public event AsyncEventHandler<WebDriverPage, TabChannelEventArgs>? EventReceived;

    /// <summary>
    /// Происходит при перехвате сетевого запроса из этой вкладки.
    /// Обработчик ДОЛЖЕН вызвать <see cref="InterceptedRequestEventArgs.Continue()"/>,
    /// <see cref="InterceptedRequestEventArgs.Abort()"/> или
    /// <see cref="InterceptedRequestEventArgs.Fulfill(InterceptedRequestFulfillment)"/>.
    /// </summary>
    public event AsyncEventHandler<WebDriverPage, InterceptedRequestEventArgs>? RequestIntercepted;

    /// <summary>
    /// Происходит непосредственно перед освобождением ресурсов страницы.
    /// Позволяет внешним компонентам (например, браузеру) закрыть вкладку через другой канал.
    /// </summary>
    internal event Func<WebDriverPage, ValueTask>? Disposing;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="WebDriverPage"/>.
    /// </summary>
    /// <param name="channel">Канал связи с вкладкой.</param>
    internal WebDriverPage(TabChannel channel)
    {
        this.channel = channel;
        MainFrame = new WebDriverFrame(channel);
        channel.EventReceived += OnChannelEvent;
    }

    /// <summary>
    /// Отправляет произвольную команду расширению через канал этой вкладки.
    /// </summary>
    internal ValueTask<BridgeMessage> SendBridgeCommandAsync(
        BridgeCommand command,
        object? payload = null,
        CancellationToken cancellationToken = default)
        => channel.SendCommandAsync(command, payload, cancellationToken);

    /// <summary>
    /// Выполняет навигацию по указанному адресу.
    /// </summary>
    /// <param name="url">Адрес страницы.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask NavigateAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        ObjectDisposedException.ThrowIf(isDisposed, this);

        await channel.SendCommandAsync(
            BridgeCommand.Navigate,
            new JsonObject { ["url"] = url.AbsoluteUri },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="NavigateAsync(Uri, CancellationToken)"/>
    public ValueTask NavigateAsync(Uri url) => NavigateAsync(url, CancellationToken.None);

    /// <summary>
    /// Выполняет навигацию по указанному адресу с параметрами загрузки.
    /// Если указан <see cref="NavigationSettings.Body"/>, браузер перейдёт на URL,
    /// но вместо ответа сервера отрендерит переданный HTML.
    /// </summary>
    /// <param name="url">Адрес страницы.</param>
    /// <param name="settings">Параметры навигации.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask NavigateAsync(Uri url, NavigationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var payload = new JsonObject { ["url"] = url.AbsoluteUri };

        if (settings.Body is not null)
            payload["body"] = settings.Body;

        await channel.SendCommandAsync(
            BridgeCommand.Navigate,
            payload,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask<JsonElement?> ExecuteAsync(string script, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return MainFrame.ExecuteAsync(script, cancellationToken);
    }

    /// <inheritdoc cref="ExecuteAsync(string, CancellationToken)"/>
    public ValueTask<JsonElement?> ExecuteAsync(string script) => ExecuteAsync(script, CancellationToken.None);

    /// <summary>
    /// Выполняет JavaScript-код во всех фреймах вкладки, включая cross-origin iframe.
    /// </summary>
    /// <param name="script">Код JavaScript для выполнения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Массив результатов из каждого фрейма.</returns>
    public async ValueTask<JsonElement?> ExecuteInAllFramesAsync(string script, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var response = await channel.SendCommandAsync(
            BridgeCommand.ExecuteScriptInFrames,
            new JsonObject { ["script"] = script },
            cancellationToken).ConfigureAwait(false);

        return response.Payload is JsonElement el ? el : null;
    }

    /// <summary>
    /// Ожидает появления элемента по CSS-селектору в любом фрейме вкладки (включая cross-origin iframe).
    /// Возвращает <see langword="true"/>, если элемент найден до истечения таймаута.
    /// </summary>
    /// <param name="selector">CSS-селектор элемента.</param>
    /// <param name="timeout">Таймаут ожидания (по умолчанию 30 секунд).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask<bool> WaitForSelectorInFramesAsync(
        string selector,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        // Экранируем кавычки в селекторе для безопасной вставки в JS-строку.
        var escapedSelector = selector.Replace("\\", "\\\\").Replace("'", "\\'");
        var script = $"document.querySelector('{escapedSelector}') !== null";

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var results = await ExecuteInAllFramesAsync(script, cancellationToken).ConfigureAwait(false);

            if (results is JsonElement arr && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.True)
                        return true;
                }
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    /// <inheritdoc />
    public ValueTask<IElement?> FindElementAsync(ElementSelector selector, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return MainFrame.FindElementAsync(selector, cancellationToken);
    }

    /// <inheritdoc cref="FindElementAsync(ElementSelector, CancellationToken)"/>
    public ValueTask<IElement?> FindElementAsync(ElementSelector selector) => FindElementAsync(selector, CancellationToken.None);

    /// <inheritdoc />
    public ValueTask<IElement[]> FindElementsAsync(ElementSelector selector, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return MainFrame.FindElementsAsync(selector, cancellationToken);
    }

    /// <inheritdoc cref="FindElementsAsync(ElementSelector, CancellationToken)"/>
    public ValueTask<IElement[]> FindElementsAsync(ElementSelector selector) => FindElementsAsync(selector, CancellationToken.None);

    /// <inheritdoc />
    public ValueTask<Uri?> GetUrlAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return MainFrame.GetUrlAsync(cancellationToken);
    }

    /// <inheritdoc cref="GetUrlAsync(CancellationToken)"/>
    public ValueTask<Uri?> GetUrlAsync() => GetUrlAsync(CancellationToken.None);

    /// <inheritdoc />
    public ValueTask<string?> GetTitleAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return MainFrame.GetTitleAsync(cancellationToken);
    }

    /// <inheritdoc cref="GetTitleAsync(CancellationToken)"/>
    public ValueTask<string?> GetTitleAsync() => GetTitleAsync(CancellationToken.None);

    /// <inheritdoc />
    public ValueTask<string?> GetContentAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return MainFrame.GetContentAsync(cancellationToken);
    }

    /// <inheritdoc cref="GetContentAsync(CancellationToken)"/>
    public ValueTask<string?> GetContentAsync() => GetContentAsync(CancellationToken.None);

    /// <summary>
    /// Делает снимок экрана вкладки.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Снимок в формате PNG, закодированный в Base64.</returns>
    public async ValueTask<string?> CaptureScreenshotAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var response = await channel.SendCommandAsync(
            BridgeCommand.CaptureScreenshot,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return response.Payload is JsonElement el ? el.GetString() : null;
    }

    /// <inheritdoc cref="CaptureScreenshotAsync(CancellationToken)"/>
    public ValueTask<string?> CaptureScreenshotAsync() => CaptureScreenshotAsync(CancellationToken.None);

    /// <inheritdoc />
    public ValueTask<IElement?> WaitForElementAsync(
        ElementSelector selector,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return MainFrame.WaitForElementAsync(selector, timeout, cancellationToken);
    }

    /// <inheritdoc cref="WaitForElementAsync(ElementSelector, TimeSpan?, CancellationToken)"/>
    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, TimeSpan? timeout)
        => WaitForElementAsync(selector, timeout, CancellationToken.None);

    /// <inheritdoc cref="WaitForElementAsync(ElementSelector, TimeSpan?, CancellationToken)"/>
    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector, CancellationToken cancellationToken)
        => WaitForElementAsync(selector, timeout: null, cancellationToken);

    /// <inheritdoc cref="WaitForElementAsync(ElementSelector, TimeSpan?, CancellationToken)"/>
    public ValueTask<IElement?> WaitForElementAsync(ElementSelector selector)
        => WaitForElementAsync(selector, timeout: null, CancellationToken.None);

    /// <summary>
    /// Ожидает завершения навигации.
    /// </summary>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask WaitForNavigationAsync(
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        await channel.SendCommandAsync(
            BridgeCommand.WaitForNavigation,
            new JsonObject { ["timeoutMs"] = (timeout ?? TimeSpan.FromSeconds(30)).TotalMilliseconds },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="WaitForNavigationAsync(TimeSpan?, CancellationToken)"/>
    public ValueTask WaitForNavigationAsync(TimeSpan? timeout)
        => WaitForNavigationAsync(timeout, CancellationToken.None);

    /// <inheritdoc cref="WaitForNavigationAsync(TimeSpan?, CancellationToken)"/>
    public ValueTask WaitForNavigationAsync(CancellationToken cancellationToken)
        => WaitForNavigationAsync(timeout: null, cancellationToken);

    /// <inheritdoc cref="WaitForNavigationAsync(TimeSpan?, CancellationToken)"/>
    public ValueTask WaitForNavigationAsync()
        => WaitForNavigationAsync(timeout: null, CancellationToken.None);

    /// <summary>
    /// Устанавливает cookie.
    /// </summary>
    /// <param name="name">Имя cookie.</param>
    /// <param name="value">Значение cookie.</param>
    /// <param name="domain">Домен.</param>
    /// <param name="path">Путь.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask SetCookieAsync(
        string name,
        string value,
        string? domain,
        string? path,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        await channel.SendCommandAsync(
            BridgeCommand.SetCookie,
            new JsonObject { ["name"] = name, ["value"] = value, ["domain"] = domain, ["path"] = path },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="SetCookieAsync(string, string, string?, string?, CancellationToken)"/>
    public ValueTask SetCookieAsync(string name, string value, string? domain, string? path)
        => SetCookieAsync(name, value, domain, path, CancellationToken.None);

    /// <inheritdoc cref="SetCookieAsync(string, string, string?, string?, CancellationToken)"/>
    public ValueTask SetCookieAsync(string name, string value, CancellationToken cancellationToken)
        => SetCookieAsync(name, value, domain: null, path: null, cancellationToken);

    /// <inheritdoc cref="SetCookieAsync(string, string, string?, string?, CancellationToken)"/>
    public ValueTask SetCookieAsync(string name, string value)
        => SetCookieAsync(name, value, domain: null, path: null, CancellationToken.None);

    /// <summary>
    /// Получает все cookies текущей страницы.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask<JsonElement?> GetCookiesAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var response = await channel.SendCommandAsync(
            BridgeCommand.GetCookies,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return response.Payload is JsonElement el ? el : null;
    }

    /// <inheritdoc cref="GetCookiesAsync(CancellationToken)"/>
    public ValueTask<JsonElement?> GetCookiesAsync() => GetCookiesAsync(CancellationToken.None);

    /// <summary>
    /// Удаляет все cookies.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask DeleteCookiesAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        await channel.SendCommandAsync(
            BridgeCommand.DeleteCookies,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="DeleteCookiesAsync(CancellationToken)"/>
    public ValueTask DeleteCookiesAsync() => DeleteCookiesAsync(CancellationToken.None);

    /// <summary>
    /// Включает или отключает перехват сетевых запросов для этой вкладки.
    /// </summary>
    /// <param name="enabled">Состояние перехвата.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask SetRequestInterceptionAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        await channel.SendCommandAsync(
            BridgeCommand.InterceptRequest,
            new JsonObject { ["enabled"] = enabled },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Вызывается <see cref="WebDriverBrowser"/> при перехвате запроса из этой вкладки.
    /// </summary>
    internal async ValueTask OnRequestInterceptedAsync(InterceptedRequestEventArgs e)
    {
        if (RequestIntercepted is { } handler)
            await handler(this, e).ConfigureAwait(false);
    }

    private async ValueTask OnChannelEvent(TabChannel sender, TabChannelEventArgs e)
    {
        if (EventReceived is { } handler)
            await handler(this, e).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;

        if (Disposing is { } disposing)
            await disposing(this).ConfigureAwait(false);

        channel.EventReceived -= OnChannelEvent;
        await channel.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Контекст JSON-сериализации для десериализации данных страницы.
/// </summary>
[JsonSerializable(typeof(string[]))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class BridgePageJsonContext : JsonSerializerContext;
