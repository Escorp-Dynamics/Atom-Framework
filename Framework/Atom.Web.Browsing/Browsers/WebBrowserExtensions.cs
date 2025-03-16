using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;

namespace Atom.Web.Browsing;

/// <summary>
/// Представляет методы расширений для <see cref="IWebBrowser"/>
/// </summary>
public static class WebBrowserExtensions
{
    /// <summary>
    /// Открывает новое окно браузера.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    /// <param name="settings">Настройки окна браузера.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask<IWebWindow> OpenWindowAsync([NotNull] this IWebBrowser browser, IWebWindowSettings settings, CancellationToken cancellationToken)
    {
        if (browser.CurrentContext is null) await browser.CreateContextAsync(cancellationToken).ConfigureAwait(false);
        return await browser.CurrentContext!.OpenWindowAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Открывает новое окно браузера.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    /// <param name="settings">Настройки окна браузера.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<IWebWindow> OpenWindowAsync(this IWebBrowser browser, IWebWindowSettings settings) => browser.OpenWindowAsync(settings, CancellationToken.None);

    /// <summary>
    /// Открывает новое окно браузера.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask<IWebWindow> OpenWindowAsync([NotNull] this IWebBrowser browser, CancellationToken cancellationToken)
    {
        if (browser.CurrentContext is null) await browser.CreateContextAsync(cancellationToken).ConfigureAwait(false);
        return await browser.CurrentContext!.OpenWindowAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Открывает новое окно браузера.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<IWebWindow> OpenWindowAsync(this IWebBrowser browser) => browser.OpenWindowAsync(CancellationToken.None);

    /// <summary>
    /// Открывает новую страницу браузера.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    /// <param name="settings">Настройки страницы браузера.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask<IWebPage> OpenPageAsync([NotNull] this IWebBrowser browser, IWebPageSettings settings, CancellationToken cancellationToken)
    {
        if (browser.CurrentContext is null) await browser.CreateContextAsync(cancellationToken).ConfigureAwait(false);
        if (browser.CurrentWindow is null) await browser.OpenWindowAsync(cancellationToken).ConfigureAwait(false);

        return await browser.CurrentWindow!.OpenPageAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Открывает новую страницу браузера.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    /// <param name="settings">Настройки страницы браузера.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<IWebPage> OpenPageAsync([NotNull] this IWebBrowser browser, IWebPageSettings settings) => browser.OpenPageAsync(settings, CancellationToken.None);

    /// <summary>
    /// Открывает новую страницу браузера.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask<IWebPage> OpenPageAsync([NotNull] this IWebBrowser browser, CancellationToken cancellationToken)
    {
        if (browser.CurrentContext is null) await browser.CreateContextAsync(cancellationToken).ConfigureAwait(false);
        if (browser.CurrentWindow is null) await browser.OpenWindowAsync(cancellationToken).ConfigureAwait(false);

        return await browser.CurrentWindow!.OpenPageAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Открывает новую страницу браузера.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<IWebPage> OpenPageAsync([NotNull] this IWebBrowser browser) => browser.OpenPageAsync(CancellationToken.None);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="headers">Заголовки, которые будут переданы с запросом навигации.</param>
    /// <param name="wait">Тип ожидания загрузки страницы.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowser browser, Uri url, IReadOnlyDictionary<string, string> headers, ReadinessState wait, CancellationToken cancellationToken)
    {
        if (browser.CurrentContext is null) await browser.CreateContextAsync(cancellationToken).ConfigureAwait(false);
        if (browser.CurrentWindow is null) await browser.OpenWindowAsync(cancellationToken).ConfigureAwait(false);
        if (browser.CurrentPage is null) await browser.OpenPageAsync(cancellationToken).ConfigureAwait(false);

        return await browser.CurrentPage!.GoToAsync(url, headers, wait, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="headers">Заголовки, которые будут переданы с запросом навигации.</param>
    /// <param name="wait">Тип ожидания загрузки страницы.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowser browser, Uri url, IReadOnlyDictionary<string, string> headers, ReadinessState wait) => browser.GoToAsync(url, headers, wait, CancellationToken.None);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="headers">Заголовки, которые будут переданы с запросом навигации.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowser browser, Uri url, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken) => browser.GoToAsync(url, headers, ReadinessState.Complete, cancellationToken);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="headers">Заголовки, которые будут переданы с запросом навигации.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowser browser, Uri url, IReadOnlyDictionary<string, string> headers) => browser.GoToAsync(url, headers, CancellationToken.None);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="referer">Заголовок реферера.</param>
    /// <param name="wait">Тип ожидания загрузки страницы.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowser browser, Uri url, string referer, ReadinessState wait, CancellationToken cancellationToken)
    {
        if (browser.CurrentContext is null) await browser.CreateContextAsync(cancellationToken).ConfigureAwait(false);
        if (browser.CurrentWindow is null) await browser.OpenWindowAsync(cancellationToken).ConfigureAwait(false);
        if (browser.CurrentPage is null) await browser.OpenPageAsync(cancellationToken).ConfigureAwait(false);

        return await browser.CurrentPage!.GoToAsync(url, referer, wait, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="referer">Заголовок реферера.</param>
    /// <param name="wait">Тип ожидания загрузки страницы.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowser browser, Uri url, string referer, ReadinessState wait) => browser.GoToAsync(url, referer, wait, CancellationToken.None);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="referer">Заголовок реферера.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowser browser, Uri url, string referer, CancellationToken cancellationToken) => browser.GoToAsync(url, referer, ReadinessState.Complete, cancellationToken);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="referer">Заголовок реферера.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowser browser, Uri url, string referer) => browser.GoToAsync(url, referer, CancellationToken.None);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="wait">Тип ожидания загрузки страницы.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowser browser, Uri url, ReadinessState wait, CancellationToken cancellationToken) => browser.GoToAsync(url, string.Empty, wait, cancellationToken);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="wait">Тип ожидания загрузки страницы.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowser browser, Uri url, ReadinessState wait) => browser.GoToAsync(url, wait, CancellationToken.None);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowser browser, Uri url, CancellationToken cancellationToken) => browser.GoToAsync(url, ReadinessState.Complete, cancellationToken);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="browser">Браузер.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowser browser, Uri url) => browser.GoToAsync(url, CancellationToken.None);
}