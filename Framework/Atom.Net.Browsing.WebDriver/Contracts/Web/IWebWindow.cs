using System.Drawing;
using System.Runtime.CompilerServices;
using Atom.Media.Audio;
using Atom.Media.Video;
using Atom.Net.Https;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Представляет окно браузера и операции управления страницами и навигацией в его пределах.
/// </summary>
public interface IWebWindow : IAsyncDisposable
{
    /// <summary>
    /// Получает текущее опубликованное состояние удаления окна.
    /// Значение является advisory snapshot и подходит для быстрой проверки жизненного цикла,
    /// но не заменяет fail-fast boundary-guards при конкурентном доступе.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Получает браузер, которому принадлежит окно.
    /// </summary>
    IWebBrowser Browser { get; }

    /// <summary>
    /// Получает открытые и еще не удаленные страницы окна.
    /// </summary>
    IEnumerable<IWebPage> Pages { get; }

    /// <summary>
    /// Получает текущую активную страницу окна.
    /// Если текущая страница удалена при еще живом окне и остаются другие страницы, текущей становится следующий живой snapshot.
    /// Если живых страниц больше не осталось, свойство сохраняет последний опубликованный snapshot до открытия новой страницы или disposal окна.
    /// Boundary-методы окна, делегирующие в текущую страницу, в этот промежуток fail-fast'ят через <see cref="ObjectDisposedException"/>.
    /// </summary>
    IWebPage CurrentPage { get; }

    /// <summary>
    /// Срабатывает при поступлении консольного сообщения из окна.
    /// </summary>
    event MutableEventHandler<IWebWindow, ConsoleMessageEventArgs>? Console;

    /// <summary>
    /// Срабатывает при перехвате исходящего запроса в пределах окна.
    /// </summary>
    event AsyncEventHandler<IWebWindow, InterceptedRequestEventArgs>? Request;

    /// <summary>
    /// Срабатывает при перехвате входящего ответа в пределах окна.
    /// </summary>
    event AsyncEventHandler<IWebWindow, InterceptedResponseEventArgs>? Response;

    /// <summary>
    /// Включает или отключает сетевой interception для всех живых страниц окна.
    /// </summary>
    ValueTask SetRequestInterceptionAsync(bool enabled, IEnumerable<string>? urlPatterns, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetRequestInterceptionAsync(bool, IEnumerable{string}?, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask SetRequestInterceptionAsync(bool enabled, IEnumerable<string>? urlPatterns) => SetRequestInterceptionAsync(enabled, urlPatterns, CancellationToken.None);

    /// <summary>
    /// Включает или отключает сетевой interception для всех живых страниц окна без URL-фильтров.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask SetRequestInterceptionAsync(bool enabled, CancellationToken cancellationToken) => SetRequestInterceptionAsync(enabled, urlPatterns: null, cancellationToken);

    /// <inheritdoc cref="SetRequestInterceptionAsync(bool, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask SetRequestInterceptionAsync(bool enabled) => SetRequestInterceptionAsync(enabled, CancellationToken.None);

    event MutableEventHandler<IWebWindow, WebLifecycleEventArgs>? DomContentLoaded;

    event MutableEventHandler<IWebWindow, WebLifecycleEventArgs>? NavigationCompleted;

    event MutableEventHandler<IWebWindow, WebLifecycleEventArgs>? PageLoaded;

    /// <summary>
    /// Активирует окно и публикует его как текущее окно браузера.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask ActivateAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="ActivateAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask ActivateAsync() => ActivateAsync(CancellationToken.None);

    /// <summary>
    /// Закрывает окно.
    /// После успешного закрытия браузер публикует следующий живой snapshot окна, если он существует.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask CloseAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="CloseAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask CloseAsync() => CloseAsync(CancellationToken.None);

    /// <summary>
    /// Открывает новую страницу.
    /// </summary>
    ValueTask<IWebPage> OpenPageAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="OpenPageAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IWebPage> OpenPageAsync() => OpenPageAsync(CancellationToken.None);

    /// <summary>
    /// Открывает новую страницу с настройками.
    /// </summary>
    ValueTask<IWebPage> OpenPageAsync(WebPageSettings settings, CancellationToken cancellationToken);

    /// <inheritdoc cref="OpenPageAsync(WebPageSettings, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IWebPage> OpenPageAsync(WebPageSettings settings) => OpenPageAsync(settings, CancellationToken.None);

    /// <summary>
    /// Удаляет все cookies всех открытых страниц текущего окна.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask ClearAllCookiesAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="ClearAllCookiesAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask ClearAllCookiesAsync() => ClearAllCookiesAsync(CancellationToken.None);

    /// <summary>
    /// Выполняет навигацию в текущей странице окна.
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
    /// Перезагружает текущую страницу окна.
    /// </summary>
    ValueTask<HttpsResponseMessage> ReloadAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="ReloadAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<HttpsResponseMessage> ReloadAsync() => ReloadAsync(CancellationToken.None);

    /// <summary>
    /// Подключает виртуальную камеру к активной странице окна.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask AttachVirtualCameraAsync(VirtualCamera camera, CancellationToken cancellationToken);

    /// <inheritdoc cref="AttachVirtualCameraAsync(VirtualCamera, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask AttachVirtualCameraAsync(VirtualCamera camera) => AttachVirtualCameraAsync(camera, CancellationToken.None);

    /// <summary>
    /// Подключает виртуальный микрофон к активной странице окна.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask AttachVirtualMicrophoneAsync(VirtualMicrophone microphone, CancellationToken cancellationToken);

    /// <inheritdoc cref="AttachVirtualMicrophoneAsync(VirtualMicrophone, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask AttachVirtualMicrophoneAsync(VirtualMicrophone microphone) => AttachVirtualMicrophoneAsync(microphone, CancellationToken.None);

    /// <summary>
    /// Возвращает страницу по имени среди еще живых страниц окна.
    /// Специальное имя current возвращает <see cref="CurrentPage"/> напрямую и не использует name-based lookup.
    /// Страницы, которые были удалены до своей проверки, пропускаются.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<IWebPage?> GetPageAsync(string name, CancellationToken cancellationToken);

    /// <inheritdoc cref="GetPageAsync(string, CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<IWebPage?> GetPageAsync(string name) => GetPageAsync(name, CancellationToken.None);

    /// <summary>
    /// Возвращает страницу по адресу среди еще живых страниц окна.
    /// Страницы, которые были удалены до своей проверки, пропускаются.
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

    /// <summary>
    /// Возвращает адрес текущей страницы окна.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<Uri?> GetUrlAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetUrlAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<Uri?> GetUrlAsync() => GetUrlAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает заголовок текущей страницы окна.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<string?> GetTitleAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetTitleAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<string?> GetTitleAsync() => GetTitleAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает прямоугольник окна.
    /// После <see cref="IAsyncDisposable.DisposeAsync"/> выбрасывает <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<Rectangle?> GetBoundingBoxAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="GetBoundingBoxAsync(CancellationToken)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ValueTask<Rectangle?> GetBoundingBoxAsync() => GetBoundingBoxAsync(CancellationToken.None);
}