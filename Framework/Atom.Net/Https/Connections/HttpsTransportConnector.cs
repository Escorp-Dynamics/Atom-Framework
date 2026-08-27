using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using Atom.Net.Tcp;
using Atom.Net.Tls;
using Atom.Net.Tls.Extensions;
using Stream = Atom.IO.Stream;

namespace Atom.Net.Https.Connections;

/// <summary>
/// Установленный транспорт вместе с результатом согласования прикладного протокола.
/// </summary>
/// <remarks>
/// Возвращается одной структурой, потому что три значения неразделимы: сокет нужен для сведений о
/// точках подключения, поток — для обмена, а согласованный протокол определяет, какой именно
/// класс соединения обязан этот транспорт принять.
/// </remarks>
internal readonly struct HttpsTransport
{
    /// <summary>Сокетный поток; остаётся нужен и после надстройки TLS.</summary>
    public required TcpStream Socket { get; init; }

    /// <summary>Прикладной поток: поток TLS либо сам сокет для незащищённого обмена.</summary>
    public required Stream Transport { get; init; }

    /// <summary>Протокол, выбранный сервером в ALPN, либо <see langword="null"/>.</summary>
    public string? NegotiatedProtocol { get; init; }

    /// <summary>Защищён ли транспорт.</summary>
    public bool IsSecure { get; init; }

    /// <summary>Согласован ли HTTP/2.</summary>
    public bool IsHttp2 => string.Equals(NegotiatedProtocol, "h2", StringComparison.Ordinal);
}

/// <summary>
/// Устанавливает транспорт для соединений HTTPS: TCP, при необходимости TLS, и согласование ALPN.
/// </summary>
/// <remarks>
/// Вынесено из классов соединений намеренно. Выбор между HTTP/1.1 и HTTP/2 делается сервером в
/// ALPN, то есть уже ПОСЛЕ рукопожатия, — а значит, решать его внутри класса, привязанного к одной
/// версии протокола, невозможно без второго подключения. Общий коннектор позволяет установить
/// транспорт ровно один раз и передать его тому соединению, о котором договорились.
///
/// Второй мотив — единственный источник истины для отпечатка. Пока настройки TLS собирались в
/// каждом классе соединения отдельно, они успели разъехаться: путь HTTP/2 терял политику
/// legacy_session_id и подстановку имени хоста в SNI. Такие расхождения не проявляются как ошибки,
/// они проявляются как «почему-то нас узнают».
/// </remarks>
internal static class HttpsTransportConnector
{
    private static readonly NamedGroup[] defaultSupportedGroups = CreateSupportedGroups();

    /// <summary>Предлагать только HTTP/1.1.</summary>
    public static readonly ReadOnlyMemory<byte>[] Http11Only = [AlpnTlsExtension.Http11];

    /// <summary>Предлагать HTTP/2 и HTTP/1.1 в порядке предпочтения браузера.</summary>
    public static readonly ReadOnlyMemory<byte>[] Http2AndHttp11 = [AlpnTlsExtension.Http2, AlpnTlsExtension.Http11];

    /// <summary>
    /// Устанавливает транспорт до целевого узла.
    /// </summary>
    /// <param name="options">Параметры соединения.</param>
    /// <param name="alpnProtocols">Протоколы, предлагаемые в ALPN, в порядке предпочтения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Установленный транспорт.</returns>
    public static async ValueTask<HttpsTransport> ConnectAsync(
        HttpsConnectionOptions options,
        IReadOnlyList<ReadOnlyMemory<byte>> alpnProtocols,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ConnectCoreAsync(options, alpnProtocols, forceTls12: false, cancellationToken).ConfigureAwait(false);
        }
        catch (TlsVersionDowngradeException)
        {
            // ★ Сервер выбрал TLS 1.2. Версия у нас решается ДО отправки ClientHello — по потолку
            // профиля, — а выбирает её сервер, и серверов без TLS 1.3 в сети по-прежнему хватает.
            // Разбирать их ответ правилами 1.3 нельзя, поэтому соединение открывается заново уже
            // потоком TLS 1.2.
            //
            // Второе соединение — цена, но цена единственная: ClientHello у обоих путей строится
            // из ОДНОГО профиля, поэтому отпечаток повторной попытки тот же, а альтернатива —
            // отказ на исправном сервере.
            return await ConnectCoreAsync(options, alpnProtocols, forceTls12: true, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask<HttpsTransport> ConnectCoreAsync(
        HttpsConnectionOptions options,
        IReadOnlyList<ReadOnlyMemory<byte>> alpnProtocols,
        bool forceTls12,
        CancellationToken cancellationToken)
    {
        var tcpStream = new TcpStream(CreateTcpSettings(options));
        Stream applicationTransport = tcpStream;
        string? negotiated = null;

        var openToken = CreateOpenToken(options.ConnectTimeout, cancellationToken, out var openTimeoutCts);

        try
        {
            // Через прокси подключаемся к НЕМУ, а до цели добираемся уже поверх — обратный порядок
            // невозможен: прокси и есть та точка, через которую нас выпускают наружу.
            if (options.UpstreamProxy is { } proxy)
            {
                await tcpStream.ConnectAsync(proxy.IdnHost, ResolveProxyPort(proxy), openToken).ConfigureAwait(false);

                // Туннель нужен только защищённому обмену: он существует ровно затем, чтобы прокси
                // не видел содержимого. Незащищённый запрос идёт прокси напрямую — с абсолютным
                // адресом в строке запроса, как это делают браузеры. Требовать CONNECT и здесь
                // значило бы получить отказ от большинства прокси.
                if (options.IsHttps) await EstablishTunnelAsync(tcpStream, options, proxy, openToken).ConfigureAwait(false);
            }
            else
            {
                await tcpStream.ConnectAsync(options.Host, options.Port, openToken).ConfigureAwait(false);
            }

            if (options.IsHttps)
            {
                var tlsSettings = CreateTlsSettings(options, alpnProtocols);

                // Версию выбираем по потолку, заданному профилем: профиль и есть описание того,
                // чем мы притворяемся, и решать это где-то ещё значило бы разъехаться с ним.
                // Ветвление однократное, на установку соединения, — на горячий путь оно не попадает.
                TlsStream tlsStream = tlsSettings.MaxVersion.HasFlag(SslProtocols.Tls13) && !forceTls12
                    ? new Tls13Stream(tcpStream, tlsSettings)
                    : new Tls12Stream(tcpStream, tlsSettings);

                try
                {
                    await tlsStream.HandshakeAsync(openToken).ConfigureAwait(false);
                }
                catch
                {
                    await tlsStream.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                // Билеты сессии приходят уже после рукопожатия, поэтому подписка ставится здесь:
                // поток умирает вместе с соединением, отписываться не нужно.
                if (tlsStream is Tls13Stream tls13 && options.SessionTicketSink is { } sink)
                    tls13.SessionTicketReceived += ticket => sink(options.Host, ticket);

                negotiated = tlsStream.NegotiatedProtocol;
                applicationTransport = tlsStream;
            }
        }
        catch (OperationCanceledException exception) when (openTimeoutCts is not null && openTimeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await DisposeOnFailureAsync(applicationTransport, tcpStream).ConfigureAwait(false);
            throw new HttpsConnectTimeoutException($"Не удалось открыть соединение с {options.Host}:{options.Port} за {options.ConnectTimeout}.", exception);
        }
        catch
        {
            await DisposeOnFailureAsync(applicationTransport, tcpStream).ConfigureAwait(false);
            throw;
        }
        finally
        {
            openTimeoutCts?.Dispose();
        }

        return new HttpsTransport
        {
            Socket = tcpStream,
            Transport = applicationTransport,
            NegotiatedProtocol = negotiated,
            IsSecure = options.IsHttps,
        };
    }

    /// <summary>
    /// Строит туннель CONNECT к целевому узлу через HTTP-прокси.
    /// </summary>
    /// <param name="tcpStream">Уже установленное соединение с прокси.</param>
    /// <param name="options">Параметры соединения.</param>
    /// <param name="proxy">Адрес прокси; учётные данные берутся из его <c>UserInfo</c>.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <remarks>
    /// Запрос отправляется открытым текстом ДО рукопожатия TLS: в этом и смысл туннеля — прокси
    /// соединяет сокеты, не видя содержимого. Поэтому и проверка подлинности сервера, и отпечаток
    /// рукопожатия остаются ровно такими же, как при прямом подключении.
    /// </remarks>
    private static async ValueTask EstablishTunnelAsync(TcpStream tcpStream, HttpsConnectionOptions options, Uri proxy, CancellationToken cancellationToken)
    {
        var target = string.Create(CultureInfo.InvariantCulture, $"{options.Host}:{options.Port}");
        var request = new StringBuilder(160);

        request.Append("CONNECT ").Append(target).Append(" HTTP/1.1\r\n");
        request.Append("Host: ").Append(target).Append("\r\n");

        if (BuildProxyAuthorization(proxy) is { } authorization)
            request.Append("Proxy-Authorization: ").Append(authorization).Append("\r\n");

        request.Append("Proxy-Connection: Keep-Alive\r\n\r\n");

        var payload = Encoding.ASCII.GetBytes(request.ToString());
        await tcpStream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);

        var status = await ReadTunnelResponseAsync(tcpStream, cancellationToken).ConfigureAwait(false);

        // Прокси отвечает 2xx только когда туннель действительно построен. Продолжить рукопожатие
        // поверх непостроенного туннеля значит отправить ClientHello в тело HTTP-ответа.
        if (status is < 200 or > 299)
            throw new IOException($"Прокси {proxy.IdnHost} отказал в туннеле к {target}: код {status}");
    }

    private static async ValueTask<int> ReadTunnelResponseAsync(TcpStream tcpStream, CancellationToken cancellationToken)
    {
        // Ответ читаем ровно до конца блока заголовков и НИ БАЙТОМ больше: всё, что идёт следом, —
        // уже данные туннеля, то есть ClientHello сервера, и прочитать их здесь значит потерять.
        var buffer = new byte[1];
        var header = new StringBuilder(128);
        var matched = 0;

        while (matched < 4)
        {
            var read = await tcpStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0) throw new IOException("Прокси закрыл соединение, не ответив на CONNECT");

            var current = (char)buffer[0];
            header.Append(current);

            matched = current switch
            {
                '\r' when matched is 0 or 2 => matched + 1,
                '\n' when matched is 1 or 3 => matched + 1,
                _ => 0,
            };

            if (header.Length > 8192) throw new IOException("Прокси прислал слишком длинный ответ на CONNECT");
        }

        var text = header.ToString();
        var firstSpace = text.IndexOf(' ', StringComparison.Ordinal);

        if (firstSpace < 0 || firstSpace + 4 > text.Length
            || !int.TryParse(text.AsSpan(firstSpace + 1, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var status))
        {
            throw new IOException("Не удалось разобрать ответ прокси на CONNECT");
        }

        return status;
    }

    private static string? BuildProxyAuthorization(Uri proxy)
    {
        if (string.IsNullOrEmpty(proxy.UserInfo)) return null;

        // UserInfo хранится в адресе процентно-кодированным: пароли с символами вроде ':' и '@'
        // иначе не записать. Отправить его без раскодирования значит отправить неверный пароль.
        var separator = proxy.UserInfo.IndexOf(':', StringComparison.Ordinal);

        var user = separator >= 0 ? proxy.UserInfo[..separator] : proxy.UserInfo;
        var password = separator >= 0 ? proxy.UserInfo[(separator + 1)..] : string.Empty;

        var credentials = Uri.UnescapeDataString(user) + ":" + Uri.UnescapeDataString(password);

        return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
    }

    private static int ResolveProxyPort(Uri proxy) => proxy.IsDefaultPort ? 8080 : proxy.Port;

    private static async ValueTask DisposeOnFailureAsync(Stream applicationTransport, TcpStream tcpStream)
    {
        if (!ReferenceEquals(applicationTransport, tcpStream))
            await applicationTransport.DisposeAsync().ConfigureAwait(false);

        await tcpStream.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Создаёт токен, ограничивающий время установки соединения.
    /// </summary>
    /// <param name="connectTimeout">Предельное время подключения.</param>
    /// <param name="cancellationToken">Внешний токен отмены.</param>
    /// <param name="timeoutCts">Источник токена, подлежащий освобождению вызывающей стороной.</param>
    /// <returns>Токен, учитывающий и отмену, и предельное время.</returns>
    public static CancellationToken CreateOpenToken(TimeSpan connectTimeout, CancellationToken cancellationToken, out CancellationTokenSource? timeoutCts)
    {
        timeoutCts = null;

        if (connectTimeout <= TimeSpan.Zero || connectTimeout == Timeout.InfiniteTimeSpan)
            return cancellationToken;

        timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(connectTimeout);
        return timeoutCts.Token;
    }

    private static TcpSettings CreateTcpSettings(HttpsConnectionOptions options)
    {
        var profileSettings = options.ProfileTcpSettings;
        if (profileSettings is null)
        {
            return new TcpSettings
            {
                IsNagleDisabled = true,
                ConnectTimeout = options.ConnectTimeout,
                AttemptTimeout = options.ConnectTimeout > TimeSpan.Zero ? options.ConnectTimeout : TimeSpan.FromSeconds(3),
                LocalEndPoint = options.LocalEndPoint,
            };
        }

        return profileSettings.Value with
        {
            ConnectTimeout = options.ConnectTimeout,
            LocalEndPoint = options.LocalEndPoint,
        };
    }

    [SuppressMessage("Security", "CA5398:Do not hardcode SslProtocols values", Justification = "Собственный путь TLS намеренно фиксирует поддерживаемые версии протокола.")]
    private static TlsSettings CreateTlsSettings(HttpsConnectionOptions options, IReadOnlyList<ReadOnlyMemory<byte>> alpnProtocols)
    {
        var profileSettings = options.ProfileTlsSettings;
        var requestedProtocols = options.SslProtocols;

        // Поддерживаются TLS 1.2 и 1.3; запрос чего-либо ещё — ошибка конфигурации, а не повод
        // молча согласиться на другую версию.
        if (requestedProtocols is not SslProtocols.None && (requestedProtocols & (SslProtocols.Tls12 | SslProtocols.Tls13)) == SslProtocols.None)
            throw new NotSupportedException("Собственный путь TLS поддерживает только TLS 1.2 и TLS 1.3");

        var defaultCipherSuites = new[]
        {
            CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
            CipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
            CipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
            CipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
        };

        var defaultExtensions = new ITlsExtension[]
        {
            new ServerNameTlsExtension { HostName = options.Host },
            new AlpnTlsExtension { Protocols = [AlpnTlsExtension.Http11] },
            new SupportedVersionsTlsExtension { Versions = [SslProtocols.Tls12] },
            new SupportedGroupsTlsExtension { Groups = defaultSupportedGroups },
            new EcPointFormatsTlsExtension { Formats = [0x00] },
            new SignatureAlgorithmsTlsExtension
            {
                Algorithms =
                [
                    SignatureAlgorithm.EcdsaSecp256r1Sha256,
                    SignatureAlgorithm.RsaPssRsaeSha256,
                    SignatureAlgorithm.RsaPkcs1Sha256,
                    SignatureAlgorithm.EcdsaSecp384r1Sha384,
                    SignatureAlgorithm.RsaPssRsaeSha384,
                    SignatureAlgorithm.RsaPkcs1Sha384,
                ]
            },
            new ExtendedMasterSecretTlsExtension { IsEnabled = true },
            new RenegotiationInfoTlsExtension(),
            new SessionTicketExtension(),
        };

        var cipherSuites = profileSettings is { } profileTls && profileTls.CipherSuites.Any()
            ? profileTls.CipherSuites
            : defaultCipherSuites;

        var extensions = MergeTlsExtensions(options.Host, alpnProtocols, defaultExtensions, profileSettings?.Extensions);

        // Предложение возобновления ставится строго после всех расширений: pre_shared_key
        // обязана замыкать приветствие (RFC 8446, §4.2.11), psk_key_exchange_modes идёт рядом.
        if (options.PskOffer is { } pskOffer)
        {
            if (extensions.All(extension => extension.Id is not 0x002d))
            {
                // См. Tls13ClientHandshake.WithPskExtensions: Modes — только явным присваиванием.
                extensions.Add(new PskKeyExchangeModesTlsExtension { Modes = [PskKeyExchangeMode.PskDheKe] });
            }

            extensions.Add(new PreSharedKeyTlsExtension
            {
                Identities = [new PskIdentity
                {
                    Identity = pskOffer.Identity,
                    ObfuscatedTicketAge = pskOffer.ObfuscatedTicketAge,
                }],

                // Заполнитель нужной длины: значение binder'а считает Tls13ClientHandshake по
                // собранному приветствию — до сборки его хэш неизвестен.
                Binders = [new byte[pskOffer.BinderLength]],
            });
        }

        var handshakeTimeout = profileSettings?.HandshakeTimeout ?? options.ConnectTimeout;
        if (handshakeTimeout <= TimeSpan.Zero || handshakeTimeout == Timeout.InfiniteTimeSpan)
        {
            handshakeTimeout = options.ConnectTimeout;
        }

        // Версию берём из профиля: он и описывает, чем мы притворяемся. Явно запрошенные
        // протоколы имеют приоритет над профилем — это ручное переопределение вызывающей стороной.
        var maxVersion = requestedProtocols is not SslProtocols.None && requestedProtocols.HasFlag(SslProtocols.Tls13)
            ? SslProtocols.Tls13
            : profileSettings?.MaxVersion ?? SslProtocols.Tls12;

        // Пустой legacy_session_id — заметный признак не-браузерного клиента: и Chrome, и Firefox
        // отправляют 32 случайных байта. Профиль вправе задать политику явно, а безпрофильный
        // путь получает браузерное поведение по умолчанию.
        var sessionIdPolicy = profileSettings?.SessionIdPolicy ?? SessionIdPolicy.Fixed32;

        return new TlsSettings
        {
            MinVersion = profileSettings?.MinVersion ?? SslProtocols.Tls12,
            MaxVersion = maxVersion,
            CipherSuites = cipherSuites,
            Extensions = extensions,
            SessionIdPolicy = sessionIdPolicy,
            CheckCertificateRevocationList = options.CheckCertificateRevocationList,
            ServerCertificateValidationCallback = options.ServerCertificateValidationCallback,
            Delay = profileSettings?.Delay ?? TimeSpan.Zero,
            HandshakeTimeout = handshakeTimeout,

            // ★ Настройки здесь СОБИРАЮТСЯ ЗАНОВО, а не уточняются, поэтому всё, что задано
            // профилем, приходится переносить поимённо. Забытое поле не ломает соединение и
            // ничего не сообщает — просто тихо теряется: перестановка расширений так и не
            // работала на живом пути, хотя в сборке сообщения была включена.
            PermuteExtensions = profileSettings?.PermuteExtensions ?? false,
            PermutationAnchors = profileSettings?.PermutationAnchors,
            PskOffer = options.PskOffer,
        };
    }

    private static List<ITlsExtension> MergeTlsExtensions(
        string host,
        IReadOnlyList<ReadOnlyMemory<byte>> alpnProtocols,
        IEnumerable<ITlsExtension> defaultExtensions,
        IEnumerable<ITlsExtension>? profileExtensions)
    {
        if (profileExtensions is null)
        {
            return [.. defaultExtensions.Select(extension => MaterializeTlsExtension(extension, host, alpnProtocols))];
        }

        // ★ К профилю дописываются ТОЛЬКО обязательные расширения, и только если их там нет.
        //
        // Прежде дописывалось всё недостающее — и это дважды дало расхождение с браузером при
        // полном совпадении всего остального: у Firefox имя сервера уезжало последним вместо
        // первого, у Safari в конце появлялся билет сессии, которого он не запрашивает вовсе.
        // Отпечаток при этом менялся, а соединение работало, так что заметить это можно было
        // только сверкой с настоящим браузером.
        //
        // Совсем отказаться от дописывания тоже нельзя: настройки, собранные вручную, вправе
        // перечислять лишь то, что важно вызывающей стороне, и без имени сервера или групп
        // рукопожатие просто не состоится. Поэтому список обязательных закрыт и мал, а профили
        // браузеров объявляют всё из него сами — проверка каталога это требует, и значит для них
        // эта ветка не срабатывает никогда.
        var merged = new List<ITlsExtension>();
        var declared = new HashSet<ushort>();

        foreach (var extension in profileExtensions)
        {
            merged.Add(MaterializeTlsExtension(extension, host, alpnProtocols));
            declared.Add(extension.Id);
        }

        foreach (var extension in defaultExtensions)
        {
            if (!IsEssentialExtension(extension.Id) || declared.Contains(extension.Id)) continue;

            merged.Add(MaterializeTlsExtension(extension, host, alpnProtocols));
        }

        return merged;
    }

    /// <summary>
    /// Сообщает, обязательно ли расширение для самой возможности рукопожатия.
    /// </summary>
    /// <param name="id">Идентификатор расширения.</param>
    /// <returns><see langword="true"/>, если без него обмен не состоится.</returns>
    /// <remarks>
    /// Список закрыт намеренно и включает только то, что нужно ЛЮБОМУ рукопожатию, любой версии:
    /// имя сервера, группы, алгоритмы подписи и ALPN. Версии и доля ключа сюда НЕ входят — они
    /// нужны лишь TLS 1.3, а дописывать их профилю эпохи TLS 1.2 значило бы менять его облик.
    /// Настройки, собранные вручную для TLS 1.3 без доли ключа, получают внятный отказ ещё до
    /// отправки.
    /// </remarks>
    private static bool IsEssentialExtension(ushort id)
        => id is 0x0000 or 0x000A or 0x000D or 0x0010;

    private static ITlsExtension MaterializeTlsExtension(ITlsExtension extension, string host, IReadOnlyList<ReadOnlyMemory<byte>> alpnProtocols)
        => extension switch
        {
            ServerNameTlsExtension serverName => new ServerNameTlsExtension
            {
                Id = serverName.Id,
                HostName = string.IsNullOrWhiteSpace(serverName.HostName) ? host : serverName.HostName,
            },

            // ALPN сужаем до пересечения: профиль описывает, что предлагает браузер, а вызывающая
            // сторона — на чём мы готовы говорить в этом подключении. Предложить протокол, который
            // мы не поддерживаем, значит получить его в ответе и развалить обмен на первом кадре;
            // предложить меньше, чем браузер, — отличаться от него в отпечатке. Пересечение с
            // сохранением порядка ПРОФИЛЯ решает обе задачи.
            AlpnTlsExtension alpn => new AlpnTlsExtension
            {
                Id = alpn.Id,
                Protocols = IntersectAlpn(alpn.Protocols, alpnProtocols),
            },
            SupportedVersionsTlsExtension supportedVersions => new SupportedVersionsTlsExtension
            {
                Id = supportedVersions.Id,
                Versions = supportedVersions.Versions.Any() ? [.. supportedVersions.Versions] : [SslProtocols.Tls12],
            },
            // ★ Копировать надо ВСЕ поля. Признак подставной группы здесь терялся, и списки
            // расходились: в долях ключа GREASE был, в группах нет — сочетание, которого у
            // браузера не бывает. Ни ja3, ни ja4 этого не показывают: оба выбрасывают GREASE
            // перед подсчётом.
            SupportedGroupsTlsExtension supportedGroups => new SupportedGroupsTlsExtension
            {
                Id = supportedGroups.Id,
                Groups = [.. supportedGroups.Groups],
                UseGrease = supportedGroups.UseGrease,
            },
            EcPointFormatsTlsExtension ecPointFormats => new EcPointFormatsTlsExtension
            {
                Id = ecPointFormats.Id,
                Formats = [.. ecPointFormats.Formats],
            },
            SignatureAlgorithmsTlsExtension signatureAlgorithms => new SignatureAlgorithmsTlsExtension
            {
                Id = signatureAlgorithms.Id,
                Algorithms = [.. signatureAlgorithms.Algorithms],
            },
            ExtendedMasterSecretTlsExtension extendedMasterSecret => new ExtendedMasterSecretTlsExtension
            {
                Id = extendedMasterSecret.Id,
                IsEnabled = extendedMasterSecret.IsEnabled,
            },
            RenegotiationInfoTlsExtension renegotiationInfo => new RenegotiationInfoTlsExtension
            {
                Id = renegotiationInfo.Id,
                RenegotiatedConnection = renegotiationInfo.RenegotiatedConnection.ToArray(),
            },
            SessionTicketExtension sessionTicket => new SessionTicketExtension
            {
                Id = sessionTicket.Id,
                Ticket = sessionTicket.Ticket.ToArray(),
            },

            // ★★ Доли ключа СОЗДАЮТСЯ ЗАНОВО, а не копируются. Профиль строится один раз на
            // обработчик и живёт тысячи запросов; беря его доли как есть, мы отправляли бы во
            // ВСЕ соединения ко всем узлам один и тот же открытый ключ X25519 и один и тот же
            // ключ инкапсуляции ML-KEM (1216 байт вместе).
            //
            // Это плохо дважды. Во-первых, теряется прямая секретность: один закрытый ключ на
            // всё время работы означает, что его раскрытие открывает ВСЕ прошлые сессии сразу,
            // тогда как весь смысл эфемерного обмена — обратное.
            //
            // Во-вторых — и для нашей задачи это важнее — постоянный открытый ключ виден в
            // ClientHello ОТКРЫТЫМ ТЕКСТОМ. Любой узел, посредник или CDN связывает по нему все
            // наши соединения в одну личность независимо от адреса, прокси и целевого сайта.
            // Ротация прокси при этом бессмысленна: 1216 постоянных байт опознают клиента
            // надёжнее любого отпечатка рукопожатия.
            KeyShareTlsExtension keyShare => new KeyShareTlsExtension
            {
                Id = keyShare.Id,
                Entries = [.. keyShare.Entries.Select(static entry => KeyShare.ForGroup(entry.Group))],
                UseGrease = keyShare.UseGrease,
            },

            // Пустышка ECH по той же причине: у настоящего браузера её содержимое случайно на
            // каждое соединение, а копия из профиля давала 241 неизменный байт в каждом
            // приветствии — такой же устойчивый признак, как постоянная доля ключа.
            EncryptedClientHelloTlsExtension ech => EncryptedClientHelloTlsExtension.CreateGrease(ech.Data.Length - EncryptedClientHelloTlsExtension.GreaseOverhead),

            _ => extension,
        };

    /// <summary>
    /// Пересекает список ALPN профиля с допустимым для этого подключения, сохраняя порядок профиля.
    /// </summary>
    /// <param name="profileProtocols">Протоколы, объявленные профилем.</param>
    /// <param name="allowedProtocols">Протоколы, на которых мы готовы говорить.</param>
    /// <returns>Итоговый список для расширения ALPN.</returns>
    internal static ReadOnlyMemory<byte>[] IntersectAlpn(
        IEnumerable<ReadOnlyMemory<byte>> profileProtocols,
        IReadOnlyList<ReadOnlyMemory<byte>> allowedProtocols)
    {
        var result = new List<ReadOnlyMemory<byte>>(allowedProtocols.Count);

        foreach (var protocol in profileProtocols)
        {
            for (var index = 0; index < allowedProtocols.Count; index++)
            {
                if (!protocol.Span.SequenceEqual(allowedProtocols[index].Span)) continue;

                result.Add(protocol);
                break;
            }
        }

        // Пустое пересечение означало бы ClientHello без ALPN — расширение, которое браузер шлёт
        // всегда. Возвращаемся к списку допустимых протоколов: лучше отличаться порядком, чем
        // отсутствием расширения целиком.
        return result.Count is 0 ? [.. allowedProtocols] : [.. result];
    }

    private static NamedGroup[] CreateSupportedGroups()
    {
        if (!IsX25519Supported())
            return [NamedGroup.Secp256r1, NamedGroup.Secp384r1];

        return [NamedGroup.X25519, NamedGroup.Secp256r1, NamedGroup.Secp384r1];
    }

    private static bool IsX25519Supported()
    {
        try
        {
            using var _ = ECDiffieHellman.Create(ECCurve.CreateFromFriendlyName("X25519"));
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }
}
