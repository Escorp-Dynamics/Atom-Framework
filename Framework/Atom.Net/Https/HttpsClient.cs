#pragma warning disable CA5398

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Text.Json.Serialization.Metadata;
using Atom.Collections;
using Atom.Net.Https.Headers;
using Atom.Net.Https.Http;
using Atom.Net.Tcp;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Https;

/// <summary>
/// Представляет средство обмена HTTPS-запросами и ответами с сервером.
/// </summary>
public class HttpsClient : HttpClient
{
    /// <summary>
    /// User-Agent клиента по умолчанию.
    /// </summary>
    public const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/137.0.0.0 Safari/537.36 Edg/137.0.0.0";

    private volatile HttpsClientHandler handler;
    private readonly bool disposeHandler;

    private Traffic traffic;

    /// <summary>
    /// Возвращает или задает контейнер файлов cookie, используемый для хранения файлов cookie сервера обработчиком.
    /// </summary>
    public CookieContainer Cookies
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => handler.CookieContainer;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => handler.CookieContainer = value;
    }

    /// <summary>
    /// Возвращает или задает сведения о прокси-сервере, используемые обработчиком.
    /// </summary>
    public IWebProxy? Proxy
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => handler.Proxy;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (!handler.isFirstRequestSended)
                handler.Proxy = value;
            else
                ResetHandler(value);
        }
    }

    /// <summary>
    /// Возвращает или задает User-Agent клиента.
    /// </summary>
    public string UserAgent
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (string.IsNullOrEmpty(value)) value = DefaultUserAgent;
            field = value;

            ResetHandler();
        }
    } = DefaultUserAgent;

    /// <summary>
    /// Возвращает или задаёт адаптер для UserAgent.
    /// </summary>
    public IUserAgentAdapter? UserAgentAdapter { get; set; } = new UserAgentAdapter();

    /// <summary>
    /// Общий трафик по всем запросам.
    /// </summary>
    public Traffic Traffic => traffic;

    /// <summary>
    /// Клиент, мимикрирующий под Google Chrome.
    /// </summary>
    public static HttpsClient Chrome => new(new HttpsClientHandler
    {
        Http2 = new Http2Settings
        {
            HeadersFormattingPolicy = HeadersFormattingPolicy.Chrome,
            Tls = new TlsSettings
            {
                CipherSuites = [
                    CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
                    CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
                    CipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
                    CipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,
                    CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
                    CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
                ],
                Extensions = [
                    TlsExtension.Grease,
                    TlsExtension.ServerName,
                    TlsExtension.ExtendedMasterSecret,
                    TlsExtension.RenegotiationInfo,
                    new SupportedGroupsTlsExtension
                    {
                        Groups = [
                            NamedGroup.X25519,
                            NamedGroup.Secp256r1,
                            NamedGroup.Secp384r1,
                        ],
                    },
                    TlsExtension.EcPointFormats,
                    new SignatureAlgorithmsTlsExtension
                    {
                        Algorithms = [
                            SignatureAlgorithm.EcdsaSecp256r1Sha256,
                            SignatureAlgorithm.RsaPssRsaeSha256,
                            SignatureAlgorithm.RsaPkcs1Sha256,
                            SignatureAlgorithm.EcdsaSecp384r1Sha384,
                            SignatureAlgorithm.RsaPssRsaeSha384,
                            SignatureAlgorithm.RsaPkcs1Sha384,
                            SignatureAlgorithm.RsaPssRsaeSha512,
                            SignatureAlgorithm.RsaPkcs1Sha512,
                            SignatureAlgorithm.RsaPkcs1Sha1,
                            SignatureAlgorithm.EcdsaSha1,
                        ],
                    },
                    TlsExtension.StatusRequest,
                    TlsExtension.SignedCertificateTimestamp,    // TODO: Не хватает реализации расширения SignedCertificateTimestampTlsExtension.
                    TlsExtension.SessionTicket,
                    new AlpnTlsExtension
                    {
                        Protocols = [
                            AlpnTlsExtension.H2,
                            AlpnTlsExtension.Http11,
                        ],
                    },
                    new SupportedVersionsTlsExtension
                    {
                        Versions = [SslProtocols.Tls13, SslProtocols.Tls12],
                    },
                    new KeyShareTlsExtension
                    {
                        Entries = [
                            KeyShare.X25519,
                        ]
                    },
                    new PskKeyExchangeModesTlsExtension
                    {
                        Modes = [PskKeyExchangeMode.PskDheKe, PskKeyExchangeMode.PskOnly],
                    },
                    new PaddingTlsExtension { Length = 0 },
                ],
            },
            Tcp = new TcpSettings
            {
                ReceiveBufferSize = 256 * 1024,
                SendBufferSize = 256 * 1024,
            },
        },
        ClientHelloValidator = ClientHelloValidator.Chrome,
    });

    /// <summary>
    /// Клиент, мимикрирующий под Microsoft Edge.
    /// </summary>
    public static HttpsClient Edge => new(new HttpsClientHandler
    {
        // TODO: Заполнить для Edge.
    });

    /// <summary>
    /// Клиент, мимикрирующий под Mozilla Firefox.
    /// </summary>
    public static HttpsClient Firefox => new(new HttpsClientHandler
    {
        // TODO: Заполнить для Firefox.
    });

    /// <summary>
    /// Клиент, мимикрирующий под Apple Safari.
    /// </summary>
    public static HttpsClient Safari => new(new HttpsClientHandler
    {
        // TODO: Заполнить для Safari.
    });

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpsClient"/>.
    /// </summary>
    /// <param name="handler">Обработчик запросов.</param>
    /// <param name="disposeHandler">Указывает, будут ли ресурсы обработчика высвобождены вместе с клиентом.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HttpsClient(HttpsClientHandler handler, bool disposeHandler)
    {
        this.handler = handler;
        this.disposeHandler = disposeHandler;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpsClient"/>.
    /// </summary>
    /// <param name="handler">Обработчик запросов.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HttpsClient(HttpsClientHandler handler) => this.handler = handler;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="HttpsClient"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HttpsClient() : this(new HttpsClientHandler()) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetHandler(IWebProxy? proxy = default)
    {
#pragma warning disable CA2000
        var handler = UserAgentAdapter?.CreateHandler(UserAgent) ?? new HttpsClientHandler();
#pragma warning restore CA2000

        handler.ActivityHeadersPropagator = this.handler.ActivityHeadersPropagator;
        handler.AllowAutoRedirect = this.handler.AllowAutoRedirect;
        handler.AutomaticDecompression = this.handler.AutomaticDecompression;
        handler.CheckCertificateRevocationList = this.handler.CheckCertificateRevocationList;
        handler.ClientCertificateOptions = this.handler.ClientCertificateOptions;
        handler.ConnectCallback = this.handler.ConnectCallback;
        handler.ConnectTimeout = this.handler.ConnectTimeout;
        handler.CookieContainer = this.handler.CookieContainer;
        handler.Credentials = this.handler.Credentials;
        handler.DefaultProxyCredentials = this.handler.DefaultProxyCredentials;
        handler.Expect100ContinueTimeout = this.handler.Expect100ContinueTimeout;
        handler.MaxAutomaticRedirections = this.handler.MaxAutomaticRedirections;
        handler.MaxConnectionsPerServer = this.handler.MaxConnectionsPerServer;
        handler.MaxRequestContentBufferSize = this.handler.MaxRequestContentBufferSize;
        handler.MaxResponseDrainSize = this.handler.MaxResponseDrainSize;
        handler.MaxResponseHeadersLength = this.handler.MaxResponseHeadersLength;
        handler.MeterFactory = this.handler.MeterFactory;
        handler.PlaintextStreamFilter = this.handler.PlaintextStreamFilter;
        handler.PooledConnectionIdleTimeout = this.handler.PooledConnectionIdleTimeout;
        handler.PooledConnectionLifetime = this.handler.PooledConnectionLifetime;
        handler.PreAuthenticate = this.handler.PreAuthenticate;
        handler.Http11 = this.handler.Http11;
        handler.Http2 = this.handler.Http2;
        handler.Http3 = this.handler.Http3;
        handler.Proxy = proxy ?? this.handler.Proxy;
        handler.RequestHeaderEncodingSelector = this.handler.RequestHeaderEncodingSelector;
        handler.ResponseDrainTimeout = this.handler.ResponseDrainTimeout;
        handler.ResponseHeaderEncodingSelector = this.handler.ResponseHeaderEncodingSelector;
        handler.ServerCertificateCustomValidationCallback = this.handler.ServerCertificateCustomValidationCallback;
        handler.SslOptions = this.handler.SslOptions;
        handler.SslProtocols = this.handler.SslProtocols;
        handler.UseCookies = this.handler.UseCookies;
        handler.UseDefaultCredentials = this.handler.UseDefaultCredentials;
        handler.UseProxy = this.handler.UseProxy;

        handler.ClientCertificates.Clear();
        handler.ClientCertificates.AddRange(this.handler.ClientCertificates);

        handler.Properties.Clear();
        handler.Properties.AddRange(this.handler.Properties);

        var oldHandler = this.handler;
        this.handler = handler;

        if (!Interlocked.CompareExchange(ref oldHandler.isReadyForDisposing, true, false) && oldHandler.activeRequests is 0)
            oldHandler.Dispose();
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new virtual async Task<HttpsResponseMessage> SendAsync([NotNull] HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var currentHandler = handler;

        try
        {
            var response = await currentHandler.SendInternalAsync(request, cancellationToken).ConfigureAwait(false);
            traffic.Add(response.Traffic);
            return response;
        }
        catch (Exception ex)
        {
            using var response = new HttpResponseMessage() { RequestMessage = request };
            return new HttpsResponseMessage(response, timer.Elapsed, ex);
        }
        finally
        {
            if (currentHandler.isReadyForDisposing && currentHandler.activeRequests is 0) currentHandler.Dispose();
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpsResponseMessage> SendAsync([NotNull] HttpsRequestBuilder request, CancellationToken cancellationToken)
        => SendAsync(request.Build(), cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpsResponseMessage> SendAsync(HttpsRequestBuilder request) => SendAsync(request, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос с десериализацией JSON.
    /// </summary>
    /// <param name="request">Данные запроса.</param>
    /// <param name="jsonTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <typeparam name="T">Тип ответа.</typeparam>
    public virtual async Task<HttpsResponseMessage<T>> SendAsync<T>(HttpRequestMessage request, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsCompleted) return new HttpsResponseMessage<T>(response, default, default);

        T? data = default;
        if (response.Content.Headers.ContentLength is 0) return new HttpsResponseMessage<T>(response, data, default);

        try
        {
            data = await response.Content.AsJsonAsync(jsonTypeInfo).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new HttpsResponseMessage<T>(response, data, ex);
        }

        return new HttpsResponseMessage<T>(response, data, default);
    }

    /// <summary>
    /// Осуществляет запрос с десериализацией JSON.
    /// </summary>
    /// <param name="request">Данные запроса.</param>
    /// <param name="jsonTypeInfo">Метаданные типа ответа.</param>
    /// <typeparam name="T">Тип ответа.</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpsResponseMessage<T>> SendAsync<T>(HttpRequestMessage request, JsonTypeInfo<T> jsonTypeInfo)
        => SendAsync(request, jsonTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="request">Параметры запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public virtual async IAsyncEnumerable<T?> SendAsyncEnumerable<T>(HttpRequestMessage request, JsonTypeInfo<T> responseTypeInfo, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is 0) yield break;

        var enumerable = response.Content.AsJsonAsyncEnumerable(responseTypeInfo, cancellationToken).ConfigureAwait(false);
        var iterator = enumerable.GetAsyncEnumerator();

        await using (iterator)
        {
            while (await iterator.MoveNextAsync())
                yield return iterator.Current;
        }
    }

    /// <summary>
    /// Осуществляет запрос и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="request">Параметры запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IAsyncEnumerable<TResponse?> SendAsyncEnumerable<TResponse>(HttpRequestMessage request, JsonTypeInfo<TResponse> responseTypeInfo) => SendAsyncEnumerable(request, responseTypeInfo, CancellationToken.None);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new virtual HttpsResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => SendAsync(request, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HttpsResponseMessage Send([NotNull] HttpsRequestBuilder request, CancellationToken cancellationToken) => Send(request.Build(), cancellationToken);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void Dispose(bool disposing)
    {
        if (disposing && disposeHandler) handler.Dispose();
        base.Dispose(disposing);
    }
}