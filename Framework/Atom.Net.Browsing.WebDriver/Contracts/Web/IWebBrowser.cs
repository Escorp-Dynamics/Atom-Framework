using System.Runtime.CompilerServices;
using Atom.Media.Audio;
using Atom.Media.Video;
using Atom.Net.Https;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет экземпляр браузера и операции управления окнами, страницами и общими ресурсами профиля.
/// </summary>
public interface IWebBrowser : IAsyncDisposable
{
    /// <summary>
    /// Получает текущее опубликованное состояние удаления браузера.
    /// Значение является advisory snapshot и подходит для быстрой проверки жизненного цикла,
    /// но не заменяет fail-fast boundary-guards при конкурентном доступе.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Получает открытые и еще не удаленные окна браузера.
    /// </summary>
    IEnumerable<IWebWindow> Windows { get; }

    /// <summary>
    /// Получает страницы всех открытых и еще не удаленных окон браузера.
    /// </summary>
    IEnumerable<IWebPage> Pages { get; }

    /// <summary>
    /// Получает текущее активное окно браузера.
    /// Если текущее окно удалено при еще живом браузере и остаются другие окна, текущим становится следующий живой snapshot.
    /// Если живых окон больше не осталось, свойство сохраняет последний опубликованный snapshot до открытия нового окна или disposal браузера.
    /// </summary>
    IWebWindow CurrentWindow { get; }

    /// <summary>
    /// Получает текущую активную страницу браузера.
    /// Если текущее окно удалено при еще живом браузере и остаются другие окна, возвращается страница следующего живого текущего окна.
    /// Если текущее окно остается живым, но его текущая страница удалена и внутри этого окна нет замены, свойство сохраняет последний опубликованный snapshot этой страницы, даже если в других окнах браузера остаются живые страницы.
    /// Если живой страницы больше не осталось, свойство сохраняет последний опубликованный snapshot до открытия новой страницы или disposal браузера.
    /// Boundary-методы браузера, делегирующие в текущую страницу, в этот промежуток fail-fast'ят через <see cref="ObjectDisposedException"/>.
    /// </summary>
    IWebPage CurrentPage { get; }

    /// <summary>
    /// Срабатывает при поступлении консольного сообщения из браузера.
    /// </summary>
    event MutableEventHandler<IWebBrowser, ConsoleMessageEventArgs>? Console;

    /// <summary>
    /// Срабатывает при перехвате исходящего браузерного запроса.
    /// </summary>
    event AsyncEventHandler<IWebBrowser, InterceptedRequestEventArgs>? Request;

    /// <summary>
    /// Срабатывает при перехвате входящего браузерного ответа.
    /// </summary>
    event AsyncEventHandler<IWebBrowser, InterceptedResponseEventArgs>? Response;

    /// <summary>
    /// Включает или отключает сетевой interception для всех живых страниц браузера.
    /// </summary>
    ValueTask SetRequestInterceptionAsync(bool enabled, IEnumerable<string>? urlPatterns, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetRequestInterceptionAsync(bool, IEnumerable{string}?, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask SetRequestInterceptionAsync(bool enabled, IEnumerable<string>? urlPatterns) => SetRequestInterceptionAsync(enabled, urlPatterns, CancellationToken.None);

    /// <summary>
    /// Включает или отключает сетевой interception для всех живых страниц браузера без URL-фильтров.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask SetRequestInterceptionAsync(bool enabled, CancellationToken cancellationToken) => SetRequestInterceptionAsync(enabled, urlPatterns: null, cancellationToken);

    /// <inheritdoc cref="SetRequestInterceptionAsync(bool, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask SetRequestInterceptionAsync(bool enabled) => SetRequestInterceptionAsync(enabled, CancellationToken.None);

    event MutableEventHandler<IWebBrowser, WebLifecycleEventArgs>? DomContentLoaded;

    event MutableEventHandler<IWebBrowser, WebLifecycleEventArgs>? NavigationCompleted;

    event MutableEventHandler<IWebBrowser, WebLifecycleEventArgs>? PageLoaded;

    /// <summary>
    /// Открывает новое окно браузера.
    /// </summary>
    ValueTask<IWebWindow> OpenWindowAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="OpenWindowAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IWebWindow> OpenWindowAsync() => OpenWindowAsync(CancellationToken.None);

    /// <summary>
    /// Открывает новое окно браузера с настройками.
    /// </summary>
    ValueTask<IWebWindow> OpenWindowAsync(WebWindowSettings settings, CancellationToken cancellationToken);

    /// <inheritdoc cref="OpenWindowAsync(WebWindowSettings, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IWebWindow> OpenWindowAsync(WebWindowSettings settings) => OpenWindowAsync(settings, CancellationToken.None);

    /// <summary>
    /// Удаляет все cookies браузера из всех его открытых окон и страниц.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask ClearAllCookiesAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="ClearAllCookiesAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask ClearAllCookiesAsync() => ClearAllCookiesAsync(CancellationToken.None);

    /// <summary>
    /// Выполняет навигацию в текущей странице браузера.
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
    /// Выполняет навигацию по настройкам.
    /// </summary>
    ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationSettings settings, CancellationToken cancellationToken);

    /// <inheritdoc cref="NavigateAsync(Uri, NavigationSettings, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<HttpsResponseMessage> NavigateAsync(Uri url, NavigationSettings settings) => NavigateAsync(url, settings, CancellationToken.None);

    /// <summary>
    /// Перезагружает текущую страницу браузера.
    /// </summary>
    ValueTask<HttpsResponseMessage> ReloadAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="ReloadAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<HttpsResponseMessage> ReloadAsync() => ReloadAsync(CancellationToken.None);

    /// <summary>
    /// Подключает виртуальную камеру к браузеру.
    /// Если браузер еще жив, но текущее окно удерживает retained disposed snapshot текущей страницы без замены, операция завершается <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask AttachVirtualCameraAsync(VirtualCamera camera, CancellationToken cancellationToken);

    /// <inheritdoc cref="AttachVirtualCameraAsync(VirtualCamera, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask AttachVirtualCameraAsync(VirtualCamera camera) => AttachVirtualCameraAsync(camera, CancellationToken.None);

    /// <summary>
    /// Подключает виртуальный микрофон к браузеру.
    /// Если браузер еще жив, но текущее окно удерживает retained disposed snapshot текущей страницы без замены, операция завершается <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask AttachVirtualMicrophoneAsync(VirtualMicrophone microphone, CancellationToken cancellationToken);

    /// <inheritdoc cref="AttachVirtualMicrophoneAsync(VirtualMicrophone, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask AttachVirtualMicrophoneAsync(VirtualMicrophone microphone) => AttachVirtualMicrophoneAsync(microphone, CancellationToken.None);

    /// <summary>
    /// Возвращает окно по заголовку текущей активной страницы окна среди еще живых окон браузера.
    /// Специальное имя current возвращает <see cref="CurrentWindow"/> напрямую и не использует title-based lookup.
    /// Окна, которые были удалены до своей проверки или уже держат retained disposed current-page snapshot, пропускаются.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<IWebWindow?> GetWindowAsync(string name, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetWindowAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IWebWindow?> GetWindowAsync(string name) => GetWindowAsync(name, CancellationToken.None);

    /// <summary>
    /// Возвращает окно, содержащее открытую страницу по адресу, среди еще живых окон браузера.
    /// Окна, которые были удалены до своей проверки, пропускаются.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<IWebWindow?> GetWindowAsync(Uri url, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetWindowAsync(Uri, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IWebWindow?> GetWindowAsync(Uri url) => GetWindowAsync(url, CancellationToken.None);

    /// <summary>
    /// Возвращает окно по связанному DOM-элементу.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<IWebWindow?> GetWindowAsync(IElement element, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetWindowAsync(IElement, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IWebWindow?> GetWindowAsync(IElement element) => GetWindowAsync(element, CancellationToken.None);

    /// <summary>
    /// Возвращает страницу по имени среди открытых страниц браузера.
    /// Специальное имя current возвращает <see cref="CurrentPage"/> напрямую и не использует name-based lookup.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<IWebPage?> GetPageAsync(string name, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetPageAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IWebPage?> GetPageAsync(string name) => GetPageAsync(name, CancellationToken.None);

    /// <summary>
    /// Возвращает страницу по адресу среди открытых страниц браузера.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<IWebPage?> GetPageAsync(Uri url, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetPageAsync(Uri, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IWebPage?> GetPageAsync(Uri url) => GetPageAsync(url, CancellationToken.None);

    /// <summary>
    /// Возвращает страницу по связанному DOM-элементу.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<IWebPage?> GetPageAsync(IElement element, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetPageAsync(IElement, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IWebPage?> GetPageAsync(IElement element) => GetPageAsync(element, CancellationToken.None);
}