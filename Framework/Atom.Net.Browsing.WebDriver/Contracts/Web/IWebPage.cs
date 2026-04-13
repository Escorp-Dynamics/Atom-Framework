using System.Drawing;
using System.Net;
using System.Runtime.CompilerServices;
using Atom.Media.Audio;
using Atom.Media.Video;
using Atom.Net.Https;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет страницу браузера и операции управления её жизненным циклом, навигацией и интеграцией с DOM.
/// </summary>
public interface IWebPage : IDomContext, IAsyncDisposable
{
    /// <summary>
    /// Получает окно, которому принадлежит страница.
    /// </summary>
    IWebWindow Window { get; }

    /// <summary>
    /// Получает основной фрейм страницы.
    /// </summary>
    IFrame MainFrame { get; }

    /// <summary>
    /// Получает или задает таймаут ожидания по умолчанию для операций страницы.
    /// </summary>
    TimeSpan WaitingTimeout { get; set; }

    /// <summary>
    /// Срабатывает при поступлении сообщения из консоли страницы.
    /// </summary>
    event MutableEventHandler<IWebPage, ConsoleMessageEventArgs>? Console;

    /// <summary>
    /// Срабатывает при перехвате исходящего запроса страницы.
    /// </summary>
    event AsyncEventHandler<IWebPage, InterceptedRequestEventArgs>? Request;

    /// <summary>
    /// Срабатывает при перехвате входящего ответа страницы.
    /// </summary>
    event AsyncEventHandler<IWebPage, InterceptedResponseEventArgs>? Response;

    /// <summary>
    /// Включает или отключает сетевой interception для страницы.
    /// </summary>
    ValueTask SetRequestInterceptionAsync(bool enabled, IEnumerable<string>? urlPatterns, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetRequestInterceptionAsync(bool, IEnumerable{string}?, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask SetRequestInterceptionAsync(bool enabled, IEnumerable<string>? urlPatterns) => SetRequestInterceptionAsync(enabled, urlPatterns, CancellationToken.None);

    /// <summary>
    /// Включает или отключает сетевой interception для страницы без URL-фильтров.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask SetRequestInterceptionAsync(bool enabled, CancellationToken cancellationToken) => SetRequestInterceptionAsync(enabled, urlPatterns: null, cancellationToken);

    /// <inheritdoc cref="SetRequestInterceptionAsync(bool, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask SetRequestInterceptionAsync(bool enabled) => SetRequestInterceptionAsync(enabled, CancellationToken.None);

    /// <summary>
    /// Срабатывает при callback-вызове из браузерного окружения страницы.
    /// </summary>
    event AsyncEventHandler<IWebPage, CallbackEventArgs>? Callback;

    event MutableEventHandler<IWebPage, WebLifecycleEventArgs>? DomContentLoaded;

    event MutableEventHandler<IWebPage, WebLifecycleEventArgs>? NavigationCompleted;

    event MutableEventHandler<IWebPage, WebLifecycleEventArgs>? PageLoaded;

    /// <summary>
    /// Срабатывает после завершения обработки callback-вызова страницы.
    /// </summary>
    event MutableEventHandler<IWebPage, CallbackFinalizedEventArgs>? CallbackFinalized;

    /// <summary>
    /// Удаляет все cookies страницы.
    /// </summary>
    ValueTask ClearAllCookiesAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="ClearAllCookiesAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask ClearAllCookiesAsync() => ClearAllCookiesAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает все cookies страницы.
    /// </summary>
    ValueTask<IEnumerable<Cookie>> GetAllCookiesAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetAllCookiesAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IEnumerable<Cookie>> GetAllCookiesAsync() => GetAllCookiesAsync(CancellationToken.None);

    /// <summary>
    /// Устанавливает cookies страницы.
    /// </summary>
    ValueTask SetCookiesAsync(IEnumerable<Cookie> cookies, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetCookiesAsync(IEnumerable{Cookie}, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask SetCookiesAsync(IEnumerable<Cookie> cookies) => SetCookiesAsync(cookies, CancellationToken.None);

    /// <summary>
    /// Выполняет навигацию на указанный адрес.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, CancellationToken cancellationToken);

    /// <inheritdoc cref="NavigateAsync(Uri, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<HttpsResponseMessage> NavigateAsync(Uri url) => NavigateAsync(url, CancellationToken.None);

    /// <summary>
    /// Выполняет навигацию с указанным типом перехода.
    /// </summary>
    ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationKind kind, CancellationToken cancellationToken);

    /// <inheritdoc cref="NavigateAsync(Uri, NavigationKind, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationKind kind) => NavigateAsync(url, kind, CancellationToken.None);

    /// <summary>
    /// Выполняет навигацию с дополнительными заголовками.
    /// </summary>
    ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken);

    /// <inheritdoc cref="NavigateAsync(Uri, IReadOnlyDictionary{string, string}, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, IReadOnlyDictionary<string, string> headers) => NavigateAsync(url, headers, CancellationToken.None);

    /// <summary>
    /// Выполняет навигацию с бинарным телом запроса.
    /// </summary>
    ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, ReadOnlyMemory<byte> body, CancellationToken cancellationToken);

    /// <inheritdoc cref="NavigateAsync(Uri, ReadOnlyMemory{byte}, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, ReadOnlyMemory<byte> body) => NavigateAsync(url, body, CancellationToken.None);

    /// <summary>
    /// Выполняет навигацию с HTML-содержимым.
    /// </summary>
    ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, string html, CancellationToken cancellationToken);

    /// <inheritdoc cref="NavigateAsync(Uri, string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, string html) => NavigateAsync(url, html, CancellationToken.None);

    /// <summary>
    /// Выполняет навигацию по полному набору настроек.
    /// </summary>
    ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationSettings settings, CancellationToken cancellationToken);

    /// <inheritdoc cref="NavigateAsync(Uri, NavigationSettings, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationSettings settings) => NavigateAsync(url, settings, CancellationToken.None);

    /// <summary>
    /// Перезагружает текущую страницу.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<HttpsResponseMessage> ReloadAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="ReloadAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<HttpsResponseMessage> ReloadAsync() => ReloadAsync(CancellationToken.None);

    /// <summary>
    /// Делает скриншот страницы.
    /// </summary>
    ValueTask<Memory<byte>> GetScreenshotAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetScreenshotAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<Memory<byte>> GetScreenshotAsync() => GetScreenshotAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает признак видимости страницы.
    /// </summary>
    ValueTask<bool> IsVisibleAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="IsVisibleAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<bool> IsVisibleAsync() => IsVisibleAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает размер viewport страницы.
    /// </summary>
    ValueTask<Size?> GetViewportSizeAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetViewportSizeAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<Size?> GetViewportSizeAsync() => GetViewportSizeAsync(CancellationToken.None);

    /// <summary>
    /// Инжектирует скрипт в страницу.
    /// </summary>
    ValueTask InjectScriptAsync(string script, CancellationToken cancellationToken);

    /// <inheritdoc cref="InjectScriptAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask InjectScriptAsync(string script) => InjectScriptAsync(script, CancellationToken.None);

    /// <summary>
    /// Инжектирует скрипт в head или body страницы.
    /// </summary>
    ValueTask InjectScriptAsync(string script, bool injectToHead, CancellationToken cancellationToken);

    /// <inheritdoc cref="InjectScriptAsync(string, bool, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask InjectScriptAsync(string script, bool injectToHead) => InjectScriptAsync(script, injectToHead, CancellationToken.None);

    /// <summary>
    /// Подключает внешний скрипт по ссылке.
    /// </summary>
    ValueTask InjectScriptLinkAsync(Uri url, CancellationToken cancellationToken);

    /// <inheritdoc cref="InjectScriptLinkAsync(Uri, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask InjectScriptLinkAsync(Uri url) => InjectScriptLinkAsync(url, CancellationToken.None);

    /// <summary>
    /// Подписывает страницу на callback-путь.
    /// </summary>
    ValueTask SubscribeAsync(string callbackPath, CancellationToken cancellationToken);

    /// <inheritdoc cref="SubscribeAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask SubscribeAsync(string callbackPath) => SubscribeAsync(callbackPath, CancellationToken.None);

    /// <summary>
    /// Отписывает страницу от callback-пути.
    /// </summary>
    ValueTask UnSubscribeAsync(string callbackPath, CancellationToken cancellationToken);

    /// <inheritdoc cref="UnSubscribeAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask UnSubscribeAsync(string callbackPath) => UnSubscribeAsync(callbackPath, CancellationToken.None);

    /// <summary>
    /// Подключает виртуальную камеру к странице.
    /// </summary>
    ValueTask AttachVirtualCameraAsync(VirtualCamera camera, CancellationToken cancellationToken);

    /// <inheritdoc cref="AttachVirtualCameraAsync(VirtualCamera, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask AttachVirtualCameraAsync(VirtualCamera camera) => AttachVirtualCameraAsync(camera, CancellationToken.None);

    /// <summary>
    /// Подключает виртуальный микрофон к странице.
    /// </summary>
    ValueTask AttachVirtualMicrophoneAsync(VirtualMicrophone microphone, CancellationToken cancellationToken);

    /// <inheritdoc cref="AttachVirtualMicrophoneAsync(VirtualMicrophone, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask AttachVirtualMicrophoneAsync(VirtualMicrophone microphone) => AttachVirtualMicrophoneAsync(microphone, CancellationToken.None);

    /// <summary>
    /// Возвращает фрейм по имени.
    /// Специальное имя MainFrame возвращает <see cref="MainFrame"/> напрямую и не использует name-based lookup.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<IFrame?> GetFrameAsync(string name, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetFrameAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IFrame?> GetFrameAsync(string name) => GetFrameAsync(name, CancellationToken.None);

    /// <summary>
    /// Возвращает фрейм по адресу.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<IFrame?> GetFrameAsync(Uri url, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetFrameAsync(Uri, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IFrame?> GetFrameAsync(Uri url) => GetFrameAsync(url, CancellationToken.None);

    /// <summary>
    /// Возвращает фрейм по связанному DOM-элементу.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<IFrame?> GetFrameAsync(IElement element, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetFrameAsync(IElement, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IFrame?> GetFrameAsync(IElement element) => GetFrameAsync(element, CancellationToken.None);
}