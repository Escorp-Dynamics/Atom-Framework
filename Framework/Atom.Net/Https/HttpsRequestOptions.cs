using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Runtime.CompilerServices;

namespace Atom.Net.Https;

/// <summary>
/// Предоставляет low-level typed accessors для browser-shaped metadata на обычных <see cref="HttpRequestMessage"/>,
/// когда caller не использует <see cref="HttpsRequestBuilder"/>.
/// </summary>
public static class HttpsRequestOptions
{
    private static readonly HttpRequestOptionsKey<RequestKind> requestKindKey = new("Atom.Net.Https.RequestKind");
    private static readonly HttpRequestOptionsKey<HttpsBrowserRequestContext> browserRequestContextKey = new("Atom.Net.Https.BrowserRequestContext");
    private static readonly HttpRequestOptionsKey<ReferrerPolicyMode> referrerPolicyKey = new("Atom.Net.Https.ReferrerPolicy");

    /// <summary>
    /// Сохраняет тип browser-shaped запроса на обычном <see cref="HttpRequestMessage"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HttpRequestMessage WithHttpsRequestKind([NotNull] this HttpRequestMessage request, RequestKind kind)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Options.Set(requestKindKey, kind);
        return request;
    }

    /// <summary>
    /// Сохраняет modulepreload semantics на обычном <see cref="HttpRequestMessage"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HttpRequestMessage WithHttpsModulePreload([NotNull] this HttpRequestMessage request)
        => request.WithHttpsRequestKind(RequestKind.ModulePreload);

    /// <summary>
    /// Сохраняет prefetch semantics на обычном <see cref="HttpRequestMessage"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HttpRequestMessage WithHttpsPrefetch([NotNull] this HttpRequestMessage request)
        => request.WithHttpsRequestKind(RequestKind.Prefetch);

    /// <summary>
    /// Сохраняет semantics для module script request graph (`script` + `cors`) на обычном <see cref="HttpRequestMessage"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HttpRequestMessage WithHttpsModuleScript([NotNull] this HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.WithHttpsRequestKind(RequestKind.Fetch);

        TryGetBrowserRequestContext(request, out var existingContext);
        request.Options.Set(browserRequestContextKey, new HttpsBrowserRequestContext
        {
            InitiatorType = existingContext?.InitiatorType,
            Destination = HttpsRequestDestination.Script,
            FetchMode = HttpsFetchMode.Cors,
            IsUserActivated = existingContext?.IsUserActivated,
            IsReload = existingContext?.IsReload,
            IsFormSubmission = existingContext?.IsFormSubmission,
            IsTopLevelNavigation = existingContext?.IsTopLevelNavigation,
        });

        return request;
    }

    /// <summary>
    /// Сохраняет browser request context на обычном <see cref="HttpRequestMessage"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HttpRequestMessage WithHttpsBrowserContext([NotNull] this HttpRequestMessage request, HttpsBrowserRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        request.Options.Set(browserRequestContextKey, context);
        return request;
    }

    /// <summary>
    /// Сохраняет explicit referrer policy на обычном <see cref="HttpRequestMessage"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HttpRequestMessage WithHttpsReferrerPolicy([NotNull] this HttpRequestMessage request, ReferrerPolicyMode policy)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Options.Set(referrerPolicyKey, policy);
        return request;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetRequestKind(HttpRequestMessage request, out RequestKind kind)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Options.TryGetValue(requestKindKey, out kind);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetBrowserRequestContext(HttpRequestMessage request, [NotNullWhen(true)] out HttpsBrowserRequestContext? context)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Options.TryGetValue(browserRequestContextKey, out context);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetReferrerPolicy(HttpRequestMessage request, out ReferrerPolicyMode? policy)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Options.TryGetValue(referrerPolicyKey, out var value))
        {
            policy = value;
            return true;
        }

        policy = null;
        return false;
    }
}