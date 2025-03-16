using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;

namespace Atom.Web.Browsing;

/// <summary>
/// Представляет методы расширения для <see cref="IWebBrowserContext"/>.
/// </summary>
public static class WebBrowserContextExtensions
{
    /// <summary>
    /// Открывает новую страницу браузера.
    /// </summary>
    /// <param name="context">Контекст браузера.</param>
    /// <param name="settings">Настройки страницы браузера.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<IWebPage> OpenPageAsync([NotNull] this IWebBrowserContext context, IWebPageSettings settings, CancellationToken cancellationToken) => context.CurrentWindow.OpenPageAsync(settings, cancellationToken);

    /// <summary>
    /// Открывает новую страницу браузера.
    /// </summary>
    /// <param name="context">Контекст браузера.</param>
    /// <param name="settings">Настройки страницы браузера.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<IWebPage> OpenPageAsync([NotNull] this IWebBrowserContext context, IWebPageSettings settings) => context.OpenPageAsync(settings, CancellationToken.None);

    /// <summary>
    /// Открывает новую страницу браузера.
    /// </summary>
    /// <param name="context">Контекст браузера.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<IWebPage> OpenPageAsync([NotNull] this IWebBrowserContext context, CancellationToken cancellationToken) => context.CurrentWindow.OpenPageAsync(cancellationToken);

    /// <summary>
    /// Открывает новую страницу браузера.
    /// </summary>
    /// <param name="context">Контекст браузера.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<IWebPage> OpenPageAsync([NotNull] this IWebBrowserContext context) => context.OpenPageAsync(CancellationToken.None);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="context">Контекст браузера.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="headers">Заголовки, которые будут переданы с запросом навигации.</param>
    /// <param name="wait">Тип ожидания загрузки страницы.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowserContext context, Uri url, IReadOnlyDictionary<string, string> headers, ReadinessState wait, CancellationToken cancellationToken) => context.CurrentPage.GoToAsync(url, headers, wait, cancellationToken);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="context">Контекст браузера.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="headers">Заголовки, которые будут переданы с запросом навигации.</param>
    /// <param name="wait">Тип ожидания загрузки страницы.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowserContext context, Uri url, IReadOnlyDictionary<string, string> headers, ReadinessState wait) => context.GoToAsync(url, headers, wait, CancellationToken.None);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="context">Контекст браузера.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="headers">Заголовки, которые будут переданы с запросом навигации.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowserContext context, Uri url, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken) => context.GoToAsync(url, headers, ReadinessState.Complete, cancellationToken);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="context">Контекст браузера.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="headers">Заголовки, которые будут переданы с запросом навигации.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowserContext context, Uri url, IReadOnlyDictionary<string, string> headers) => context.GoToAsync(url, headers, CancellationToken.None);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="context">Контекст браузера.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="referer">Заголовок реферера.</param>
    /// <param name="wait">Тип ожидания загрузки страницы.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowserContext context, Uri url, string referer, ReadinessState wait, CancellationToken cancellationToken) => context.CurrentPage.GoToAsync(url, referer, wait, cancellationToken);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="context">Контекст браузера.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="referer">Заголовок реферера.</param>
    /// <param name="wait">Тип ожидания загрузки страницы.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowserContext context, Uri url, string referer, ReadinessState wait) => context.GoToAsync(url, referer, wait, CancellationToken.None);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="context">Контекст браузера.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="referer">Заголовок реферера.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowserContext context, Uri url, string referer, CancellationToken cancellationToken) => context.GoToAsync(url, referer, ReadinessState.Complete, cancellationToken);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="context">Контекст браузера.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="referer">Заголовок реферера.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowserContext context, Uri url, string referer) => context.GoToAsync(url, referer, CancellationToken.None);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="context">Контекст браузера.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="wait">Тип ожидания загрузки страницы.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowserContext context, Uri url, ReadinessState wait, CancellationToken cancellationToken) => context.GoToAsync(url, string.Empty, wait, cancellationToken);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="context">Контекст браузера.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="wait">Тип ожидания загрузки страницы.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowserContext context, Uri url, ReadinessState wait) => context.GoToAsync(url, wait, CancellationToken.None);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="context">Контекст браузера.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowserContext context, Uri url, CancellationToken cancellationToken) => context.GoToAsync(url, ReadinessState.Complete, cancellationToken);

    /// <summary>
    /// Открывает адрес страницы.
    /// </summary>
    /// <param name="context">Контекст браузера.</param>
    /// <param name="url">Ссылка страницы.</param>
    /// <returns>Статус-код ответа страницы.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<HttpStatusCode> GoToAsync([NotNull] this IWebBrowserContext context, Uri url) => context.GoToAsync(url, CancellationToken.None);
}