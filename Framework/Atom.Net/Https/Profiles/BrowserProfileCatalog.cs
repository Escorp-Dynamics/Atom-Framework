using System.Net;
using System.Security.Authentication;
using Atom.Net.Https.Http2;
using Atom.Net.Https.Http3;
using Atom.Net.Quic;
using Atom.Net.Tcp;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;

namespace Atom.Net.Https.Profiles;

/// <summary>
/// Каталог встроенных browser profiles.
/// </summary>
public static class BrowserProfileCatalog
{
    public static BrowserProfile CreateChromeDesktopLinux()
        => CreateChromiumProfile(
            "Chrome Desktop Linux",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

    public static BrowserProfile CreateChromeDesktopWindows()
        => CreateChromiumProfile(
            "Chrome Desktop Windows",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

    public static BrowserProfile CreateEdgeDesktopWindows()
        => CreateChromiumProfile(
            "Edge Desktop Windows",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0");

    public static BrowserProfile CreateFirefoxDesktop()
        => new()
        {
            DisplayName = "Firefox Desktop",
            UserAgent = "Mozilla/5.0 (X11; Linux x86_64; rv:154.0) Gecko/20100101 Firefox/154.0",
            PreferredHttpVersion = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Tcp = CreateTcpSettings(),
            Tls = CreateFirefoxTlsSettings(),
            Headers = CreateFirefoxHeaders(),
            Http2 = Http2ProfileCatalog.CreateFirefox(),
            Http3 = Http3ProfileCatalog.CreateFirefox(),
            QuicTransport = QuicTransportParameters.CreateFirefox(),
        };

    public static BrowserProfile CreateSafariDesktopMacOs()
        => new()
        {
            DisplayName = "Safari Desktop macOS",
            UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15",
            PreferredHttpVersion = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Tcp = CreateTcpSettings(),
            Tls = CreateSafariTlsSettings(),
            Headers = CreateSafariHeaders(),
            Http2 = Http2ProfileCatalog.CreateSafari(),
        };

    /// <summary>
    /// Создаёт профиль Chromium с транспортом TLS 1.3 и HTTP/2.
    /// </summary>
    /// <param name="displayName">Отображаемое имя профиля.</param>
    /// <param name="userAgent">Строка агента.</param>
    /// <returns>Профиль браузера.</returns>
    /// <remarks>
    /// Отдельная фабрика, а не флаг у существующей: профиль TLS 1.2 остаётся рабочим путём, и
    /// переключать его версию «на месте» нельзя — это изменило бы поведение всех вызывающих.
    ///
    /// ВАЖНО: каждый вызов создаёт СВЕЖУЮ долю ключа X25519. Профиль нельзя кешировать и
    /// переиспользовать между соединениями: эфемерный ключ на то и эфемерный, а его повторное
    /// использование не только ослабляет защиту, но и само по себе аномально для браузера.
    /// </remarks>
    public static BrowserProfile CreateChromiumTls13Profile(string displayName, string userAgent)
        => new()
        {
            DisplayName = displayName,
            UserAgent = userAgent,
            PreferredHttpVersion = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Tcp = CreateTcpSettings(),
            Tls = CreateChromeTls13Settings(),
            Headers = CreateChromiumHeaders(),
            Http2 = Http2ProfileCatalog.CreateChrome(),

            // ★ QUIC объявляется ЯВНО. Прежде эти два поля оставались пустыми, а слой соединений
            // подставлял хромиумовские умолчания — для Chrome верные, но точно так же они
            // доставались Safari, у которого QUIC не описан вовсе. Профиль Safari уходил в
            // HTTP/3 с параметрами транспорта и SETTINGS движка Chromium, то есть менял личность
            // на полпути: по TCP это Safari, а стоило серверу объявить alt-svc — уже Chrome.
            Http3 = Http3ProfileCatalog.CreateChrome(),
            QuicTransport = new Quic.QuicTransportParameters(),
        };

    /// <summary>
    /// Создаёт профиль Chrome для Windows с транспортом TLS 1.3.
    /// </summary>
    /// <returns>Профиль браузера.</returns>
    public static BrowserProfile CreateChromeDesktopWindowsTls13()
        => CreateChromiumTls13Profile(
            "Chrome Desktop Windows (TLS 1.3)",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

    /// <summary>
    /// Создаёт профиль Edge для Windows с транспортом TLS 1.3.
    /// </summary>
    /// <returns>Профиль браузера.</returns>
    /// <remarks>
    /// Транспорт у Edge и Chrome общий — это один и тот же BoringSSL с одной настройкой, — и
    /// различается только наблюдаемая прикладная часть: строка агента и подсказки клиента, где
    /// появляется бренд Microsoft Edge. Отдельный транспортный профиль для Edge был бы вымыслом.
    /// </remarks>
    public static BrowserProfile CreateEdgeDesktopWindowsTls13()
        => CreateChromiumTls13Profile(
            "Edge Desktop Windows (TLS 1.3)",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0");

    /// <summary>
    /// Создаёт профиль Chrome для Linux с транспортом TLS 1.3.
    /// </summary>
    /// <returns>Профиль браузера.</returns>
    public static BrowserProfile CreateChromeDesktopLinuxTls13()
        => CreateChromiumTls13Profile(
            "Chrome Desktop Linux (TLS 1.3)",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

    /// <summary>
    /// Chrome для Android.
    /// </summary>
    /// <returns>Профиль браузера.</returns>
    /// <remarks>
    /// Транспортный отпечаток — отпечаток ДВИЖКА, и он тот же, что у настольного Chrome: на
    /// Android работает тот же BoringSSL с той же настройкой. Отличается наблюдаемая прикладная
    /// часть — строка агента и подсказки клиента, где платформа и признак мобильности говорят
    /// сами за себя.
    ///
    /// Единственное транспортное различие, которое вносит устройство, — порядок наборов шифров на
    /// оборудовании БЕЗ аппаратного AES: там ChaCha20 становится первым. См.
    /// <see cref="WithChaChaPreference"/>.
    /// </remarks>
    public static BrowserProfile CreateChromeAndroid()
        => CreateChromiumTls13Profile(
            "Chrome Android",
            "Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36") with
        {
            IsMobile = true,
            ClientHintsPlatform = "Android",
        };

    /// <summary>
    /// Edge для Android.
    /// </summary>
    /// <returns>Профиль браузера.</returns>
    public static BrowserProfile CreateEdgeAndroid()
        => CreateChromiumTls13Profile(
            "Edge Android",
            "Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Mobile Safari/537.36 EdgA/151.0.0.0") with
        {
            IsMobile = true,
            ClientHintsPlatform = "Android",
        };

    /// <summary>
    /// Samsung Internet.
    /// </summary>
    /// <returns>Профиль браузера.</returns>
    /// <remarks>
    /// Оболочка над Chromium: транспорт неотличим от Chrome, различается только строка агента.
    /// </remarks>
    public static BrowserProfile CreateSamsungInternet()
        => CreateChromiumTls13Profile(
            "Samsung Internet",
            "Mozilla/5.0 (Linux; Android 15; SAMSUNG SM-S928B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/27.0 Chrome/151.0.0.0 Mobile Safari/537.36") with
        {
            IsMobile = true,
            ClientHintsPlatform = "Android",
        };

    /// <summary>
    /// Firefox для Android.
    /// </summary>
    /// <returns>Профиль браузера.</returns>
    /// <remarks>
    /// Тот же Gecko и та же библиотека NSS, что и на настольной системе, — отпечаток рукопожатия
    /// совпадает. Подсказок клиента Firefox не отправляет вовсе, поэтому мобильность выражается
    /// только строкой агента.
    /// </remarks>
    public static BrowserProfile CreateFirefoxAndroid()
        => new()
        {
            DisplayName = "Firefox Android",
            UserAgent = "Mozilla/5.0 (Android 15; Mobile; rv:154.0) Gecko/154.0 Firefox/154.0",
            PreferredHttpVersion = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Tcp = CreateTcpSettings(),
            Tls = CreateFirefoxTlsSettings(),
            Headers = CreateFirefoxHeaders(),
            Http2 = Http2ProfileCatalog.CreateFirefox(),
            Http3 = Http3ProfileCatalog.CreateFirefox(),
            QuicTransport = QuicTransportParameters.CreateFirefox(),
            IsMobile = true,
        };

    /// <summary>
    /// Safari для iOS.
    /// </summary>
    /// <returns>Профиль браузера.</returns>
    /// <remarks>
    /// Транспорт общий с настольным Safari: у Apple одна библиотека TLS на все платформы, и
    /// отдельного отпечатка у iOS нет. Слепок сверен с записанным обменом Safari 18.3 на iOS —
    /// совпал ja3 целиком (см. <see cref="CreateSafariDesktopMacOs"/>).
    ///
    /// Устройства Apple под рукой нет, поэтому проверить своим замером это нельзя; при появлении
    /// доступа эталон снимается приёмником ClientHello (<see cref="Tls.ClientHelloInspector"/>)
    /// на видимом из сети адресе — способ не зависит ни от платформы, ни от средств разработчика.
    /// </remarks>
    public static BrowserProfile CreateSafariIos()
        => new()
        {
            DisplayName = "Safari iOS",
            UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.5 Mobile/15E148 Safari/604.1",
            PreferredHttpVersion = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Tcp = CreateTcpSettings(),
            Tls = CreateSafariTlsSettings(),
            Headers = CreateSafariHeaders(),
            Http2 = Http2ProfileCatalog.CreateSafari(),
            IsMobile = true,
        };

    /// <summary>
    /// Chrome для iOS.
    /// </summary>
    /// <returns>Профиль браузера.</returns>
    /// <remarks>
    /// На iOS сторонним браузерам запрещён собственный движок, поэтому Chrome там — это WebKit с
    /// другой строкой агента. Транспортный отпечаток берётся от Safari, а не от Chromium: взяв
    /// хромиумовский, мы получили бы сочетание, которого на устройстве не бывает.
    /// </remarks>
    public static BrowserProfile CreateChromeIos()
        => CreateSafariIos() with
        {
            DisplayName = "Chrome iOS",
            UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/151.0.0.0 Mobile/15E148 Safari/604.1",
        };

    /// <summary>
    /// Переставляет наборы шифров под устройство без аппаратного ускорения AES.
    /// </summary>
    /// <param name="profile">Исходный профиль.</param>
    /// <returns>Профиль с ChaCha20 во главе списка.</returns>
    /// <remarks>
    /// Не украшение: библиотека TLS браузера выбирает порядок ПО ОБОРУДОВАНИЮ. Там, где AES
    /// исполняется командами процессора, он идёт первым; где нет — первым идёт ChaCha20, который
    /// на таких устройствах заметно быстрее. Заявив недорогой телефон и предложив при этом
    /// порядок «сначала AES», мы сообщаем о себе то, чего на этом устройстве не бывает.
    /// </remarks>
    public static BrowserProfile WithChaChaPreference(BrowserProfile profile)
    {
        var suites = profile.Tls.CipherSuites.ToList();

        MoveToFront(suites, CipherSuite.TLS_CHACHA20_POLY1305_SHA256);
        MoveToFront(suites, CipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256, after: CipherSuite.TLS_CHACHA20_POLY1305_SHA256);
        MoveToFront(suites, CipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256, after: CipherSuite.TLS_CHACHA20_POLY1305_SHA256);

        return profile with { Tls = profile.Tls with { CipherSuites = suites } };
    }

    private static void MoveToFront(List<CipherSuite> suites, CipherSuite suite, CipherSuite? after = null)
    {
        var index = suites.IndexOf(suite);
        if (index < 0) return;

        suites.RemoveAt(index);

        var target = after is null ? 0 : suites.IndexOf(after.Value) + 1;
        suites.Insert(Math.Clamp(target, 0, suites.Count), suite);
    }

    public static BrowserProfile CreateChromiumProfile(string displayName, string userAgent)
        => new()
        {
            DisplayName = displayName,
            UserAgent = userAgent,
            PreferredHttpVersion = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Tcp = CreateTcpSettings(),
            Tls = CreateChromiumTlsSettings(),
            Headers = CreateChromiumHeaders(),
            Http2 = Http2ProfileCatalog.CreateChrome(),
        };

    private static BrowserHeaderProfile CreateChromiumHeaders()
        => CreateHeaderProfile(useClientHints: true, GetChromiumDefaultReferrerPolicy());

    private static BrowserHeaderProfile CreateFirefoxHeaders()
        => CreateHeaderProfile(useClientHints: false, GetFirefoxDefaultReferrerPolicy());

    private static BrowserHeaderProfile CreateSafariHeaders()
        => CreateHeaderProfile(useClientHints: false, GetSafariDefaultReferrerPolicy());

    private static BrowserHeaderProfile CreateHeaderProfile(bool useClientHints, ReferrerPolicyMode defaultReferrerPolicy)
        => new()
        {
            DefaultRequestKind = RequestKind.Fetch,
            DefaultReferrerPolicy = defaultReferrerPolicy,
            UseOriginalHeaderCase = true,
            UsePreserveHeaderOrder = true,
            UseConnectionKeepAlive = true,
            UseClientHints = useClientHints,
            EmitAcceptEncoding = true,
            EmitAcceptLanguage = true,
        };

    private static ReferrerPolicyMode GetChromiumDefaultReferrerPolicy()
        => ReferrerPolicyMode.StrictOriginWhenCrossOrigin;

    private static ReferrerPolicyMode GetFirefoxDefaultReferrerPolicy()
        => ReferrerPolicyMode.StrictOriginWhenCrossOrigin;

    private static ReferrerPolicyMode GetSafariDefaultReferrerPolicy()
        => ReferrerPolicyMode.StrictOriginWhenCrossOrigin;

    private static TcpSettings CreateTcpSettings()
        => new()
        {
            IsNagleDisabled = true,
            UseHappyEyeballsAlternating = true,
            AttemptTimeout = TimeSpan.FromSeconds(3),
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };

    private static TlsSettings CreateChromiumTlsSettings()
        => new()
        {
            MinVersion = SslProtocols.Tls12,
            MaxVersion = SslProtocols.Tls12,
            CipherSuites =
            [
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
                CipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,

                // Последние шесть наборов современным соединением выбраны не будут — сервер
                // возьмёт что-то из начала списка, — но браузер их предлагает, и их отсутствие
                // видно в отпечатке так же ясно, как лишний набор.
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA,
                CipherSuite.TLS_RSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_RSA_WITH_AES_256_GCM_SHA384,
                CipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA,
                CipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA,
            ],
            // Chrome, Firefox и Safari всегда отправляют 32-байтный legacy_session_id — в TLS 1.2
            // это идентификатор сессии, в TLS 1.3 он сохранён ради совместимости с посредниками.
            // Пустое поле здесь — заметный признак не-браузерного клиента, поэтому политика
            // объявляется в профиле, а не выбирается слоем соединений.
            SessionIdPolicy = SessionIdPolicy.Fixed32,
            Extensions = CreateChromiumExtensions(),
            HandshakeTimeout = TimeSpan.FromSeconds(10),
        };

    /// <summary>
    /// Настройки TLS современного Firefox.
    /// </summary>
    /// <remarks>
    /// Состав снят с настоящего Firefox 154 приёмом его ClientHello на локальном слушателе.
    /// От Chromium отличается принципиально, и различия не косметические:
    ///
    /// GREASE НЕТ ВООБЩЕ — ни в шифрах, ни в расширениях, ни в группах. Вставив его «для
    /// правдоподобия», мы получили бы клиента, которым Firefox не бывает.
    ///
    /// Порядок наборов другой: ChaCha20 стоит ВТОРЫМ, сразу после AES-128.
    ///
    /// Есть два расширения, которых у Chromium нет: delegated_credentials (0x0022) и
    /// record_size_limit (0x001C). И нет application_settings — оно чисто хромиумовское.
    ///
    /// Группы включают P-521 и конечнополевые ffdhe2048/ffdhe3072, а доли ключа отправляются
    /// сразу для ТРЁХ групп против двух у Chromium.
    /// </remarks>
    private static TlsSettings CreateFirefoxTlsSettings()
        => new()
        {
            MinVersion = SslProtocols.Tls12,
            MaxVersion = SslProtocols.Tls13,
            CipherSuites =
            [
                CipherSuite.TLS_AES_128_GCM_SHA256,
                CipherSuite.TLS_CHACHA20_POLY1305_SHA256,
                CipherSuite.TLS_AES_256_GCM_SHA384,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
                CipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA,
                CipherSuite.TLS_RSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_RSA_WITH_AES_256_GCM_SHA384,
                CipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA,
                CipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA,
            ],
            SessionIdPolicy = SessionIdPolicy.Fixed32,
            Extensions = CreateFirefoxExtensions(),
            HandshakeTimeout = TimeSpan.FromSeconds(10),
            CheckCertificateRevocationList = false,
        };

    /// <summary>
    /// Настройки TLS современного Safari.
    /// </summary>
    /// <remarks>
    /// ★ Состав взят из <c lang="text">refraction-networking/utls</c>, профиль <c lang="text">HelloSafari_26_3</c> — это
    /// записанный с настоящего браузера ClientHello, а не описание по памяти. Прежний профиль был
    /// догадкой: TLS 1.2, четыре набора шифров, ни GREASE, ни постквантового гибрида — то есть
    /// Safari, какого не существует.
    ///
    /// Устройства Apple под рукой нет, поэтому проверить замером мы это не можем, и источник
    /// назван прямо. Он, однако, первичный: профили utls собираются из перехваченных сообщений
    /// браузера.
    ///
    /// Отличий от Chromium и Gecko здесь три, и все наблюдаемы. Порядок наборов TLS 1.3 у Safari
    /// СВОЙ: 0x1302 впереди 0x1303 и 0x1301 — у Chrome он 1301-1302-1303, у Firefox 1301-1303-1302.
    /// В хвосте живёт тройка 3DES, которой нет ни у кого другого. И расширений всего
    /// четырнадцать: ни ECH, ни application_settings, ни record_size_limit Safari не отправляет.
    /// </remarks>
    private static TlsSettings CreateSafariTlsSettings()
        => new()
        {
            MinVersion = SslProtocols.Tls12,
            MaxVersion = SslProtocols.Tls13,
            CipherSuites =
            [
                CipherSuite.TLS_AES_128_GCM_SHA256,
                CipherSuite.TLS_AES_256_GCM_SHA384,
                CipherSuite.TLS_CHACHA20_POLY1305_SHA256,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_CBC_SHA,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA,
                CipherSuite.TLS_RSA_WITH_AES_256_GCM_SHA384,
                CipherSuite.TLS_RSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA,
                CipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_3DES_EDE_CBC_SHA,
                CipherSuite.TLS_ECDHE_RSA_WITH_3DES_EDE_CBC_SHA,
                CipherSuite.TLS_RSA_WITH_3DES_EDE_CBC_SHA,
            ],
            SessionIdPolicy = SessionIdPolicy.Fixed32,
            Extensions = CreateSafariExtensions(),
            HandshakeTimeout = TimeSpan.FromSeconds(10),
            CheckCertificateRevocationList = false,
        };

    /// <summary>
    /// Настройки TLS 1.3 под современный Chrome.
    /// </summary>
    /// <remarks>
    /// Отдельный набор, а не правка существующего: профиль TLS 1.2 остаётся рабочим путём для
    /// совместимости, и ломать его переключением версии нельзя. Здесь важны три вещи, каждая из
    /// которых наблюдаема сервером до единой строки скриптов: наборы шифров 1.3 идут первыми,
    /// ALPN предлагает <c lang="text">h2</c> перед <c lang="text">http/1.1</c>, а key_share несёт X25519 — основную группу
    /// браузера. Отсутствие любой из них выдаёт не-браузерный клиент.
    /// </remarks>
    private static TlsSettings CreateChromeTls13Settings()
        => new()
        {
            MinVersion = SslProtocols.Tls12,
            MaxVersion = SslProtocols.Tls13,
            CipherSuites =
            [
                CipherSuite.TLS_AES_128_GCM_SHA256,
                CipherSuite.TLS_AES_256_GCM_SHA384,
                CipherSuite.TLS_CHACHA20_POLY1305_SHA256,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
                CipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
                CipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,

                // Последние шесть наборов современным соединением выбраны не будут — сервер
                // возьмёт что-то из начала списка, — но браузер их предлагает, и их отсутствие
                // видно в отпечатке так же ясно, как лишний набор.
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA,
                CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA,
                CipherSuite.TLS_RSA_WITH_AES_128_GCM_SHA256,
                CipherSuite.TLS_RSA_WITH_AES_256_GCM_SHA384,
                CipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA,
                CipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA,
            ],
            // Chrome, Firefox и Safari всегда отправляют 32-байтный legacy_session_id — в TLS 1.2
            // это идентификатор сессии, в TLS 1.3 он сохранён ради совместимости с посредниками.
            // Пустое поле здесь — заметный признак не-браузерного клиента, поэтому политика
            // объявляется в профиле, а не выбирается слоем соединений.
            SessionIdPolicy = SessionIdPolicy.Fixed32,
            Extensions = CreateChromeTls13Extensions(),
            HandshakeTimeout = TimeSpan.FromSeconds(10),

            // ★ Порядок расширений браузер меняет на КАЖДОМ соединении, поэтому постоянный ja3 —
            // сам по себе признак подделки. Замер: три подряд подключения одного и того же Chrome
            // к одному узлу дали три разных хэша. Подробности — в ClientHelloExtensionPermutation.
            PermuteExtensions = true,

            // Онлайн-проверка отзыва ОТКЛЮЧЕНА намеренно, и это не послабление безопасности, а
            // соответствие поведению браузера. Замер на реальных серверах: 5732 мс против 359 мс —
            // блокирующий запрос к серверу отзыва на каждое рукопожатие несовместим с пропускной
            // способностью. При этом Chrome такие запросы не делает: он полагается на OCSP stapling
            // и собственные списки отзыва, поэтому наш OCSP-трафик был бы наблюдаемой аномалией и
            // работал бы ПРОТИВ мимикрии. Цепочка и подпись CertificateVerify проверяются всегда.
            CheckCertificateRevocationList = false,
        };

    /// <summary>
    /// Расширения ClientHello для профиля TLS 1.3 под Chrome.
    /// </summary>
    /// <remarks>
    /// Состав снят с настоящего браузера и сверен по отпечатку JA4. Порядок соответствует
    /// наблюдаемому, но опираться только на него не следует: начиная с Chrome 110 порядок
    /// расширений рандомизируется на каждое соединение, поэтому решающим является СОСТАВ и
    /// значения. Именно поэтому JA4, который расширения сортирует, устойчив, а JA3 у современного
    /// Chrome «плавает».
    ///
    /// Каждое расширение здесь наблюдаемо сервером в первом же пакете, и отсутствие любого делает
    /// клиента отличимым от заявленного браузера — даже когда соединение полностью работоспособно.
    /// </remarks>
    private static ITlsExtension[] CreateChromeTls13Extensions()
        =>
        [
            // GREASE открывает и закрывает список: браузер намеренно вставляет неизвестные
            // значения, чтобы посредники не полагались на жёсткий набор расширений. Первое
            // расширение пустое, последнее несёт один нулевой байт — так делает браузер.
            GreaseTlsExtension.Create(slot: 0),
            new ExtendedMasterSecretTlsExtension { IsEnabled = true },
            new RenegotiationInfoTlsExtension(),
            new StatusRequestTlsExtension(),
            // ★ Список СНЯТ С ПРОВОДА, а не составлен по памяти. Замер 2026-09-09: наш ClientHello
            // отдавал 9 алгоритмов и обрывался на rsa_pkcs1_sha384, тогда как настоящий Chrome
            // отдаёт 11 плюс подставную запись впереди. Оба хвостовых алгоритма выбраны быть не
            // могут — SHA-512 сертификатов в обращении практически нет, — но их отсутствие меняет
            // третью часть JA4 (у нас 806a8c22fdea против cb7bf5808d99 у браузера), а её как раз и
            // сверяют: она считается по СПИСКУ ПОДПИСЕЙ и, в отличие от порядка расширений, не
            // «плавает» от соединения к соединению. Проверять снимком tls.peet.ws — см. заметки.
            CreateGreasedSignatureAlgorithmsExtension(
                // Постквантовые подписи ML-DSA современный Chrome ставит первыми.
                SignatureAlgorithm.MlDsa44,
                SignatureAlgorithm.MlDsa65,
                SignatureAlgorithm.MlDsa87,
                SignatureAlgorithm.EcdsaSecp256r1Sha256,
                SignatureAlgorithm.RsaPssRsaeSha256,
                SignatureAlgorithm.RsaPkcs1Sha256,
                SignatureAlgorithm.EcdsaSecp384r1Sha384,
                SignatureAlgorithm.RsaPssRsaeSha384,
                SignatureAlgorithm.RsaPkcs1Sha384,
                SignatureAlgorithm.RsaPssRsaeSha512,
                SignatureAlgorithm.RsaPkcs1Sha512),
            ApplicationSettingsTlsExtension.Create(AlpnTlsExtension.Http2),
            new ServerNameTlsExtension(),
            new AlpnTlsExtension { Protocols = [AlpnTlsExtension.Http2, AlpnTlsExtension.Http11] },

            // Шифрование ClientHello браузер объявляет ВСЕГДА, подставляя правдоподобную пустышку
            // там, где настоящая конфигурация сервера неизвестна.
            EncryptedClientHelloTlsExtension.CreateGrease(),
            new SupportedVersionsTlsExtension { Versions = [SslProtocols.Tls13, SslProtocols.Tls12] },
            new CompressCertificateTlsExtension { Algorithms = [CertificateCompressionAlgorithm.Brotli] },
            new SignedCertificateTimestampTlsExtension(),
            new SessionTicketExtension(),
            new EcPointFormatsTlsExtension { Formats = [0x00] },
            CreateGreasedSupportedGroupsExtension(CreateChromeSupportedGroups()),

            // Доли ключа создаются при построении профиля: эфемерный ключ обязан быть свежим на
            // каждое соединение, поэтому профиль запрашивается заново, а не кешируется.
            new KeyShareTlsExtension { Entries = CreateChromeKeyShares(), UseGrease = true },
            new PskKeyExchangeModesTlsExtension { Modes = [PskKeyExchangeMode.PskDheKe] },

            // Список доверенных корней Chromium отправляет со 143-й версии. Он объявительный —
            // на нашу проверку цепочки не влияет, — но входит в счётчик расширений JA4.
            new TrustAnchorsTlsExtension(),
            GreaseTlsExtension.Create(slot: 1, payloadLength: 1),
        ];

    /// <summary>
    /// Настройки TLS для QUIC под Chrome.
    /// </summary>
    /// <param name="transportParameters">Закодированные параметры транспорта QUIC.</param>
    /// <returns>Настройки рукопожатия.</returns>
    /// <remarks>
    /// От обычного профиля отличается ровно тем, что требует сам QUIC (RFC 9001, §8):
    ///
    /// ALPN объявляет <c lang="text">h3</c> — иного прикладного протокола поверх QUIC у браузера нет;
    /// supported_versions содержит ТОЛЬКО TLS 1.3, потому что QUIC с более ранними версиями не
    /// определён; legacy_session_id обязан быть ПУСТЫМ — совместимость с посредниками, ради
    /// которой он заполняется поверх TCP, здесь бессмысленна, а непустое поле сервер расценит как
    /// нарушение; и добавляется расширение с параметрами транспорта, без которого соединение
    /// невозможно в принципе.
    ///
    /// Всё остальное — наборы шифров, группы, подписи, GREASE — остаётся тем же: это тот же
    /// браузер, и отпечаток рукопожатия у него общий.
    /// </remarks>
    public static TlsSettings CreateChromeQuicTlsSettings(ReadOnlyMemory<byte> transportParameters)
    {
        var extensions = new List<ITlsExtension>();

        foreach (var extension in CreateChromeTls13Extensions())
        {
            extensions.Add(extension switch
            {
                AlpnTlsExtension alpn => new AlpnTlsExtension { Id = alpn.Id, Protocols = [Http3Protocol] },
                SupportedVersionsTlsExtension versions => new SupportedVersionsTlsExtension { Id = versions.Id, Versions = [SslProtocols.Tls13] },
                _ => extension,
            });
        }

        // Параметры транспорта ставим перед замыкающим GREASE — там же, где их отправляет браузер.
        extensions.Insert(extensions.Count - 1, new QuicTransportParametersTlsExtension { Data = transportParameters });

        return CreateChromeTls13Settings() with
        {
            Extensions = extensions,
            SessionIdPolicy = SessionIdPolicy.Empty,
        };
    }

    /// <summary>Имя протокола HTTP/3 для ALPN.</summary>
    private static ReadOnlyMemory<byte> Http3Protocol { get; } = "h3"u8.ToArray();

    /// <summary>
    /// Группы, предлагаемые современным Chrome.
    /// </summary>
    /// <remarks>
    /// Гибрид X25519 с ML-KEM-768 браузер ставит первым и сразу отправляет для него долю ключа.
    /// Когда примитив ML-KEM на платформе недоступен, объявлять группу нельзя: сервер вправе её
    /// выбрать, и завершить рукопожатие мы тогда не сможем. Отпечаток в этом случае отличается от
    /// браузерного, но соединение остаётся рабочим — это честнее, чем обещать невыполнимое.
    /// </remarks>
    private static NamedGroup[] CreateChromeSupportedGroups()
        => KeyShare.IsMLKemSupported
            ? [NamedGroup.X25519MLKem768, NamedGroup.X25519, NamedGroup.Secp256r1, NamedGroup.Secp384r1]
            : [NamedGroup.X25519, NamedGroup.Secp256r1, NamedGroup.Secp384r1];

    /// <summary>
    /// Доли ключа, отправляемые Chrome вместе с ClientHello.
    /// </summary>
    /// <remarks>
    /// Браузер отправляет их сразу для двух групп — гибридной и X25519, — чтобы сервер мог
    /// завершить обмен за один круг независимо от своего выбора.
    /// </remarks>
    private static KeyShare[] CreateChromeKeyShares()
        => KeyShare.IsMLKemSupported
            ? [KeyShare.X25519MLKem768, KeyShare.X25519]
            : [KeyShare.X25519];

    private static ITlsExtension[] CreateChromiumExtensions()
        =>
        [
            new ServerNameTlsExtension(),
            new ExtendedMasterSecretTlsExtension { IsEnabled = true },
            new RenegotiationInfoTlsExtension(),
            CreateSupportedGroupsExtension(
                NamedGroup.X25519,
                NamedGroup.Secp256r1,
                NamedGroup.Secp384r1),
            new EcPointFormatsTlsExtension { Formats = [0x00] },
            new SessionTicketExtension(),
            // Браузеры предлагают h2 независимо от версии TLS: выбор прикладного протокола и
            // версия рукопожатия — независимые вещи, и клиент, объявляющий только http/1.1,
            // отличим от браузера по одному этому расширению.
            new AlpnTlsExtension { Protocols = [AlpnTlsExtension.Http2, AlpnTlsExtension.Http11] },
            CreateSignatureAlgorithmsExtension(
                SignatureAlgorithm.EcdsaSecp256r1Sha256,
                SignatureAlgorithm.RsaPssRsaeSha256,
                SignatureAlgorithm.RsaPkcs1Sha256,
                SignatureAlgorithm.EcdsaSecp384r1Sha384,
                SignatureAlgorithm.RsaPssRsaeSha384,
                SignatureAlgorithm.RsaPkcs1Sha384,
                SignatureAlgorithm.RsaPkcs1Sha1),
            new SupportedVersionsTlsExtension { Versions = [SslProtocols.Tls12] },
        ];

    /// <summary>
    /// Расширения ClientHello современного Firefox.
    /// </summary>
    /// <remarks>
    /// Порядок и состав сняты с настоящего Firefox 154. В отличие от Chromium, порядок здесь
    /// УСТОЙЧИВ — Firefox его не перемешивает, поэтому у него стабилен и JA3.
    /// </remarks>
    private static ITlsExtension[] CreateFirefoxExtensions()
        =>
        [
            // Имя сервера идёт ПЕРВЫМ — так его ставит настоящий Firefox. Само по себе это
            // выглядит мелочью, но порядок расширений входит в ja3 целиком: не объявив имя здесь,
            // профиль получал бы его дописанным в конец из значений по умолчанию, и слепок
            // расходился бы с браузером при полном совпадении всего остального.
            new ServerNameTlsExtension(),
            new ExtendedMasterSecretTlsExtension { IsEnabled = true },
            new RenegotiationInfoTlsExtension(),
            CreateSupportedGroupsExtension(CreateFirefoxSupportedGroups()),
            new EcPointFormatsTlsExtension { Formats = [0x00] },
            new SessionTicketExtension(),
            new AlpnTlsExtension { Protocols = [AlpnTlsExtension.Http2, AlpnTlsExtension.Http11] },
            new StatusRequestTlsExtension(),
            new DelegatedCredentialsTlsExtension
            {
                Algorithms =
                [
                    SignatureAlgorithm.EcdsaSecp256r1Sha256,
                    SignatureAlgorithm.EcdsaSecp384r1Sha384,
                    SignatureAlgorithm.EcdsaSecp521r1Sha512,
                    (SignatureAlgorithm)0x0203,
                ]
            },
            new SignedCertificateTimestampTlsExtension(),
            new KeyShareTlsExtension { Entries = CreateFirefoxKeyShares() },
            new SupportedVersionsTlsExtension { Versions = [SslProtocols.Tls13, SslProtocols.Tls12] },
            CreateSignatureAlgorithmsExtension(
                SignatureAlgorithm.EcdsaSecp256r1Sha256,
                SignatureAlgorithm.EcdsaSecp384r1Sha384,
                SignatureAlgorithm.EcdsaSecp521r1Sha512,
                SignatureAlgorithm.RsaPssRsaeSha256,
                SignatureAlgorithm.RsaPssRsaeSha384,
                SignatureAlgorithm.RsaPssRsaeSha512,
                SignatureAlgorithm.RsaPkcs1Sha256,
                SignatureAlgorithm.RsaPkcs1Sha384,
                SignatureAlgorithm.RsaPkcs1Sha512,
                (SignatureAlgorithm)0x0203,
                (SignatureAlgorithm)0x0201),
            new PskKeyExchangeModesTlsExtension { Modes = [PskKeyExchangeMode.PskDheKe] },
            new RecordSizeLimitTlsExtension { Limit = 0x4001 },
            new CompressCertificateTlsExtension { Algorithms = [CertificateCompressionAlgorithm.Zlib, CertificateCompressionAlgorithm.Brotli, CertificateCompressionAlgorithm.Zstd] },
            EncryptedClientHelloTlsExtension.CreateGrease(),
        ];

    /// <summary>
    /// Группы, предлагаемые Firefox.
    /// </summary>
    /// <remarks>
    /// Шире, чем у Chromium: помимо постквантового гибрида и кривых NIST есть P-521 и две
    /// конечнополевые группы. Их отсутствие так же заметно, как лишний набор шифров.
    /// </remarks>
    private static NamedGroup[] CreateFirefoxSupportedGroups()
        => KeyShare.IsMLKemSupported
            ? [NamedGroup.X25519MLKem768, NamedGroup.X25519, NamedGroup.Secp256r1, NamedGroup.Secp384r1, NamedGroup.Secp521r1, NamedGroup.Ffdhe2048, NamedGroup.Ffdhe3072]
            : [NamedGroup.X25519, NamedGroup.Secp256r1, NamedGroup.Secp384r1, NamedGroup.Secp521r1, NamedGroup.Ffdhe2048, NamedGroup.Ffdhe3072];

    /// <summary>
    /// Доли ключа, отправляемые Firefox.
    /// </summary>
    /// <remarks>
    /// Их ТРИ, а не две: гибрид, X25519 и P-256. Лишний оборот рукопожатия дороже лишних
    /// полутора килобайт в первом пакете.
    /// </remarks>
    private static KeyShare[] CreateFirefoxKeyShares()
        => KeyShare.IsMLKemSupported
            ? [KeyShare.X25519MLKem768, KeyShare.X25519, KeyShare.P256]
            : [KeyShare.X25519, KeyShare.P256];

    /// <summary>
    /// Расширения ClientHello современного Safari.
    /// </summary>
    /// <remarks>
    /// Порядок и состав — из профиля <c lang="text">HelloSafari_26_3</c> проекта utls. GREASE открывает и
    /// закрывает список, как у движков Chromium, но набор между ними другой: у Safari нет ни
    /// шифрования ClientHello, ни application_settings, ни record_size_limit, ни делегированных
    /// удостоверений. Зато есть сжатие сертификата и постквантовый гибрид в долях ключа.
    /// </remarks>
    private static ITlsExtension[] CreateSafariExtensions()
        =>
        [
            GreaseTlsExtension.Create(slot: 0),
            new ServerNameTlsExtension(),
            new ExtendedMasterSecretTlsExtension { IsEnabled = true },
            new RenegotiationInfoTlsExtension(),
            CreateGreasedSupportedGroupsExtension(CreateSafariSupportedGroups()),
            new EcPointFormatsTlsExtension { Formats = [0x00] },
            new AlpnTlsExtension { Protocols = [AlpnTlsExtension.Http2, AlpnTlsExtension.Http11] },
            new StatusRequestTlsExtension(),
            CreateSignatureAlgorithmsExtension(
                SignatureAlgorithm.EcdsaSecp256r1Sha256,
                SignatureAlgorithm.RsaPssRsaeSha256,
                SignatureAlgorithm.RsaPkcs1Sha256,
                SignatureAlgorithm.EcdsaSecp384r1Sha384,

                // ★ ДВАЖДЫ, и это не опечатка: Safari действительно перечисляет
                // rsa_pss_rsae_sha384 два раза подряд. Особенность его библиотеки — и ровно она
                // не давала сойтись отпечатку, пока список считался набором без повторов.
                SignatureAlgorithm.RsaPssRsaeSha384,
                SignatureAlgorithm.RsaPssRsaeSha384,
                SignatureAlgorithm.RsaPkcs1Sha384,
                SignatureAlgorithm.RsaPssRsaeSha512,
                SignatureAlgorithm.RsaPkcs1Sha512,
                SignatureAlgorithm.RsaPkcs1Sha1),
            new SignedCertificateTimestampTlsExtension(),
            new KeyShareTlsExtension { Entries = CreateSafariKeyShares(), UseGrease = true },
            new PskKeyExchangeModesTlsExtension { Modes = [PskKeyExchangeMode.PskDheKe] },
            new SupportedVersionsTlsExtension { Versions = [SslProtocols.Tls13, SslProtocols.Tls12] },
            new CompressCertificateTlsExtension { Algorithms = [CertificateCompressionAlgorithm.Zlib] },
            GreaseTlsExtension.Create(slot: 1),

            // Дополнение вместо билета сессии. Safari билетов не запрашивает вовсе — в замере на
            // его месте стоит именно padding, — и это заметное отличие от всех остальных.
            new PaddingTlsExtension { Length = 0 },
        ];

    /// <summary>
    /// Группы, предлагаемые Safari.
    /// </summary>
    /// <remarks>
    /// X25519 и три кривые NIST. Ни постквантового гибрида, ни конечнополевых групп: по замеру
    /// Safari 18.3 их не объявляет.
    /// </remarks>
    private static NamedGroup[] CreateSafariSupportedGroups()
        => [NamedGroup.X25519, NamedGroup.Secp256r1, NamedGroup.Secp384r1, NamedGroup.Secp521r1];

    /// <summary>
    /// Доли ключа, отправляемые Safari.
    /// </summary>
    /// <remarks>
    /// Ровно одна — X25519. Кривые NIST объявляются, но доли для них не отправляются, поэтому
    /// сервер, предпочитающий P-384 или P-521, попросит повторить приветствие.
    /// </remarks>
    private static KeyShare[] CreateSafariKeyShares() => [KeyShare.X25519];

    private static SignatureAlgorithmsTlsExtension CreateSignatureAlgorithmsExtension(params SignatureAlgorithm[] algorithms)
        => new()
        {
            Algorithms = algorithms,
        };

    /// <summary>
    /// Создаёт список подписей с подставной записью в начале.
    /// </summary>
    /// <param name="algorithms">Алгоритмы браузера в порядке приоритета.</param>
    /// <returns>Готовое расширение.</returns>
    /// <remarks>
    /// Отдельный помощник по той же причине, что и у групп: подставную запись в подписях шлёт
    /// только Chromium. У Firefox и Safari её нет, и добавить её значило бы выдать себя.
    /// </remarks>
    private static SignatureAlgorithmsTlsExtension CreateGreasedSignatureAlgorithmsExtension(params SignatureAlgorithm[] algorithms)
        => new()
        {
            Algorithms = algorithms,
            UseGrease = true,
        };

    /// <summary>
    /// Создаёт список групп с подставной записью в начале.
    /// </summary>
    /// <param name="groups">Группы браузера.</param>
    /// <returns>Готовое расширение.</returns>
    /// <remarks>
    /// Отдельный помощник, потому что подставную группу шлют не все: у Firefox её нет ни здесь,
    /// ни в долях ключа, и добавить её значило бы выдать себя.
    /// </remarks>
    private static SupportedGroupsTlsExtension CreateGreasedSupportedGroupsExtension(params NamedGroup[] groups)
        => new()
        {
            Groups = groups,
            UseGrease = true,
        };

    private static SupportedGroupsTlsExtension CreateSupportedGroupsExtension(params NamedGroup[] groups)
        => new()
        {
            Groups = groups,
        };
}