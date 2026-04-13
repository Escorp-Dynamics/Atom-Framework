using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Atom.Architect.Builders;
using Atom.Buffers;

namespace Atom.Net.Https;

/// <summary>
/// Представляет primary typed builder для browser-shaped HTTP-запросов на active H1 path.
/// </summary>
public partial class HttpsRequestBuilder : IBuilder<HttpRequestMessage, HttpsRequestBuilder>
{
    private readonly Dictionary<string, string> headers = [];

    private HttpMethod method = HttpMethod.Get;
    private Uri? url;
    private Version version = HttpVersion.Version11;
    private byte versionPolicy = (byte)HttpVersionPolicy.RequestVersionOrLower;
    private HttpContent? content;
    private RequestKind requestKind;
    private HttpsBrowserRequestContext? browserRequestContext;
    private Uri? referrer;
    private ReferrerPolicyMode? referrerPolicy;

    /// <summary>
    /// Устанавливает метод запроса.
    /// </summary>
    /// <param name="method">Метод запроса.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual HttpsRequestBuilder WithMethod(HttpMethod method)
    {
        Volatile.Write(ref this.method, method);
        return this;
    }

    /// <summary>
    /// Устанавливает адрес запроса.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual HttpsRequestBuilder WithUrl(UrlBuilder? url)
    {
        Volatile.Write(ref this.url, url?.Build());
        return this;
    }

    /// <summary>
    /// Устанавливает адрес запроса.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual HttpsRequestBuilder WithUrl(Uri? url)
    {
        Volatile.Write(ref this.url, url);
        return this;
    }

    /// <summary>
    /// Устанавливает версию запроса.
    /// </summary>
    /// <param name="version">Версия запроса.</param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual HttpsRequestBuilder WithVersion(Version version)
    {
        Volatile.Write(ref this.version, version);
        return this;
    }

    /// <summary>
    /// Устанавливает политику версий запроса.
    /// </summary>
    /// <param name="versionPolicy">Политика версий.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual HttpsRequestBuilder WithVersionPolicy(HttpVersionPolicy versionPolicy)
    {
        Volatile.Write(ref this.versionPolicy, (byte)versionPolicy);
        return this;
    }

    /// <summary>
    /// Добавляет заголовки запроса.
    /// </summary>
    /// <param name="headers">Заголовки запроса.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual HttpsRequestBuilder WithHeaders([NotNull] params IEnumerable<KeyValuePair<string, string>> headers)
    {
        foreach (var header in headers) this.headers.Add(header.Key, header.Value);
        return this;
    }

    /// <summary>
    /// Добавляет заголовки запроса.
    /// </summary>
    /// <param name="headers">Заголовки запроса.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HttpsRequestBuilder WithHeaders(HttpRequestHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        foreach (var header in headers)
            this.headers[header.Key] = string.Join(", ", header.Value);

        return this;
    }

    /// <summary>
    /// Добавляет заголовок запроса.
    /// </summary>
    /// <param name="name">Имя заголовка.</param>
    /// <param name="value">Значение заголовка.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual HttpsRequestBuilder WithHeader(string name, string value)
    {
        headers.TryAdd(name, value);
        return this;
    }

    /// <summary>
    /// Устанавливает контент запроса.
    /// </summary>
    /// <param name="content">Контент запроса.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual HttpsRequestBuilder WithContent(HttpContent content)
    {
        Volatile.Write(ref this.content, content);
        return this;
    }

    /// <summary>
    /// Устанавливает browser-shaped тип запроса.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual HttpsRequestBuilder WithRequestKind(RequestKind requestKind)
    {
        this.requestKind = requestKind;
        return this;
    }

    /// <summary>
    /// Устанавливает modulepreload semantics для link rel=modulepreload.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual HttpsRequestBuilder WithModulePreload()
    {
        requestKind = RequestKind.ModulePreload;
        return this;
    }

    /// <summary>
    /// Устанавливает prefetch semantics для link rel=prefetch.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual HttpsRequestBuilder WithPrefetch()
    {
        requestKind = RequestKind.Prefetch;
        return this;
    }

    /// <summary>
    /// Устанавливает semantics для module script request graph (`script` + `cors`).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual HttpsRequestBuilder WithModuleScript()
    {
        requestKind = RequestKind.Fetch;

        var existingContext = browserRequestContext;
        browserRequestContext = new HttpsBrowserRequestContext
        {
            InitiatorType = existingContext?.InitiatorType,
            Destination = HttpsRequestDestination.Script,
            FetchMode = HttpsFetchMode.Cors,
            IsUserActivated = existingContext?.IsUserActivated,
            IsReload = existingContext?.IsReload,
            IsFormSubmission = existingContext?.IsFormSubmission,
            IsTopLevelNavigation = existingContext?.IsTopLevelNavigation,
        };

        return this;
    }

    /// <summary>
    /// Устанавливает browser request context.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual HttpsRequestBuilder WithBrowserContext([NotNull] HttpsBrowserRequestContext browserRequestContext)
    {
        ArgumentNullException.ThrowIfNull(browserRequestContext);
        Volatile.Write(ref this.browserRequestContext, browserRequestContext);
        return this;
    }

    /// <summary>
    /// Устанавливает referer запроса.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual HttpsRequestBuilder WithReferrer(Uri? referrer)
    {
        Volatile.Write(ref this.referrer, referrer);
        return this;
    }

    /// <summary>
    /// Устанавливает referrer policy override.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual HttpsRequestBuilder WithReferrerPolicy(ReferrerPolicyMode referrerPolicy)
    {
        this.referrerPolicy = referrerPolicy;
        return this;
    }

    /// <summary>
    /// Устанавливает контент запроса.
    /// </summary>
    /// <param name="client">Контент запроса.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual HttpsRequestBuilder WithClientDefaults([NotNull] HttpClient client) => WithVersion(client.DefaultRequestVersion)
        .WithVersionPolicy(client.DefaultVersionPolicy);

    /// <inheritdoc/>
    [Pooled]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Reset()
    {
        headers.Clear();

        Volatile.Write(ref method, HttpMethod.Get);
        Volatile.Write(ref url, default);
        Volatile.Write(ref version, HttpVersion.Version11);
        Volatile.Write(ref versionPolicy, (byte)HttpVersionPolicy.RequestVersionOrLower);
        Volatile.Write(ref content, default);
        requestKind = RequestKind.Unknown;
        Volatile.Write(ref browserRequestContext, default);
        Volatile.Write(ref referrer, default);
        referrerPolicy = default;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual HttpRequestMessage Build() => BuildHttpsRequest();

    /// <summary>
    /// Создаёт browser-shaped HTTPS-запрос.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual HttpsRequestMessage BuildHttpsRequest()
    {
        var message = new HttpsRequestMessage(method, url)
        {
            Version = Volatile.Read(ref version),
            VersionPolicy = (HttpVersionPolicy)Volatile.Read(ref versionPolicy),
            Content = content,
            Kind = requestKind,
            Context = Volatile.Read(ref browserRequestContext),
            ReferrerPolicy = referrerPolicy,
        };

        if (Volatile.Read(ref referrer) is { } requestReferrer)
            message.Headers.Referrer = requestReferrer;

        foreach (var header in headers) message.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return message;
    }

    /// <summary>
    /// Создаёт новый экземпляр строителя.
    /// </summary>
    /// <param name="method">Метод запроса.</param>
    /// <param name="url">Адрес запроса.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HttpsRequestBuilder Create(HttpMethod method, UrlBuilder? url) => Rent().WithMethod(method).WithUrl(url);

    /// <summary>
    /// Создаёт новый экземпляр строителя.
    /// </summary>
    /// <param name="method">Метод запроса.</param>
    /// <param name="url">Адрес запроса.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HttpsRequestBuilder Create(HttpMethod method, Uri? url) => Rent().WithMethod(method).WithUrl(url);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HttpsRequestBuilder Create() => Create(HttpMethod.Get, default);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static IBuilder<HttpRequestMessage> IBuilder<HttpRequestMessage>.Create() => Create();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static IBuilder IBuilder.Create() => Create();

    /// <summary>
    /// Преобразует <see cref="Uri"/> в <see cref="HttpsRequestBuilder"/>.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HttpsRequestBuilder FromUri(Uri url) => Create(HttpMethod.Get, url);

    /// <summary>
    /// Преобразует <see cref="HttpsRequestBuilder"/> в <see cref="HttpRequestMessage"/>.
    /// </summary>
    /// <param name="builder">Экземпляр строителя запроса.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HttpRequestMessage ToHttpRequestMessage([NotNull] HttpsRequestBuilder builder) => builder.Build();

    /// <summary>
    /// Преобразует <see cref="HttpsRequestBuilder"/> в <see cref="HttpsRequestMessage"/>.
    /// </summary>
    /// <param name="builder">Экземпляр строителя запроса.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HttpsRequestMessage ToHttpsRequestMessage([NotNull] HttpsRequestBuilder builder) => builder.BuildHttpsRequest();

    /// <summary>
    /// Преобразует <see cref="Uri"/> в <see cref="HttpsRequestBuilder"/>.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator HttpsRequestBuilder(Uri url) => FromUri(url);

    /// <summary>
    /// Преобразует <see cref="HttpsRequestBuilder"/> в <see cref="HttpRequestMessage"/>.
    /// </summary>
    /// <param name="builder">Экземпляр строителя запроса.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator HttpRequestMessage(HttpsRequestBuilder builder) => ToHttpRequestMessage(builder);

    /// <summary>
    /// Преобразует <see cref="HttpsRequestBuilder"/> в <see cref="HttpsRequestMessage"/>.
    /// </summary>
    /// <param name="builder">Экземпляр строителя запроса.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator HttpsRequestMessage(HttpsRequestBuilder builder) => ToHttpsRequestMessage(builder);
}