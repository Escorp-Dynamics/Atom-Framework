using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;


namespace Atom.Net.Tls;

/// <summary>
/// Клиентское рукопожатие TLS 1.3 на уровне СООБЩЕНИЙ, без привязки к уровню записей.
/// </summary>
/// <remarks>
/// Вынесено из <see cref="Tls13Stream"/> потому, что рукопожатие TLS 1.3 нужно ДВУМ разным
/// транспортам. Поверх TCP сообщения едут в записях TLS; в QUIC записей нет вовсе — те же самые
/// сообщения переносятся кадрами CRYPTO, а шифрованием занимается сам QUIC. Всё остальное —
/// транскрипт, расписание ключей, проверка сертификата и подписи, выбор доли ключа — совпадает
/// до байта.
///
/// Продублировать этот код для QUIC было бы худшим решением: расходятся именно такие копии, а
/// расхождение здесь — это либо провал проверки подлинности сервера, либо молчаливое согласие с
/// неправильной цепочкой.
///
/// Класс НЕ владеет шифрованием: он лишь выдаёт секреты трафика. Как превратить секрет в ключи и
/// на какой уровень их поставить, решает транспорт — у записей TLS и у пакетов QUIC это разные
/// вещи.
/// </remarks>
/// <param name="settings">Настройки TLS: расширения ClientHello, политика проверки сертификата.</param>
public sealed class Tls13ClientHandshake(in TlsSettings settings) : IDisposable
{
    /// <summary>Длина вектора инициализации для наборов AEAD, применяемых в TLS 1.3.</summary>
    public const int IvLength = 12;

    /// <summary>
    /// Предел размера распакованного сообщения Certificate.
    /// </summary>
    /// <remarks>
    /// Цепочка сертификатов реального сервера укладывается в десятки килобайт. Ограничение
    /// защищает от сообщения, объявляющего гигантскую исходную длину: буфер выделяется ДО
    /// распаковки, и без предела одно такое сообщение исчерпало бы память.
    /// </remarks>
    private const int MaxCertificateMessageLength = 1 << 20;

    private readonly HandshakeTranscript transcript = new();
    private readonly List<X509Certificate2> serverCertificates = [];

    private IReadOnlyList<KeyShare> clientKeyShares = [];
    private KeyShare? clientKeyShare;
    private string? sniHost;
    private byte[]? certificateTranscriptHash;
    private byte[]? serverFinishedTranscriptHash;
    private byte[]? handshakeSecret;
    private byte[]? sharedSecret;

    /// <summary>Накопитель для сообщения рукопожатия, разрезанного между записями.</summary>
    private byte[] pending = [];
    private int pendingLength;

    /// <summary>Контекст запроса сертификата клиента; не null, если сервер его прислал.</summary>
    private byte[]? certificateRequestContext;

    /// <summary>Случайное число и идентификатор сессии первого приветствия.</summary>
    /// <remarks>Повторное приветствие после HelloRetryRequest обязано их сохранить.</remarks>
    private byte[]? helloRandom;
    private byte[]? helloSessionId;

    /// <summary>Настройки TLS.</summary>
    public TlsSettings Settings { get; } = settings;

    /// <summary>
    /// Расширения в том порядке, в каком они уходят на провод.
    /// </summary>
    /// <remarks>
    /// Порядок вычисляется ОДИН раз на рукопожатие, а не на каждое построение сообщения. Второе
    /// приветствие после HelloRetryRequest обязано совпадать с первым во всём, кроме доли ключа и
    /// эха cookie (RFC 8446, §4.1.2), и перестановка при повторной сборке сорвала бы проверку.
    /// </remarks>
    private IEnumerable<Extensions.ITlsExtension> OrderedExtensions { get; } = settings.PermuteExtensions
        ? ClientHelloExtensionPermutation.Apply(settings.Extensions, settings.PermutationAnchors)
        : settings.Extensions;

    /// <summary>Хэш-функция, заданная согласованным набором шифров.</summary>
    public HashAlgorithmName HashAlgorithm { get; private set; } = HashAlgorithmName.SHA256;

    /// <summary>Длина ключа AEAD, заданная согласованным набором шифров.</summary>
    public int AeadKeyLength { get; private set; } = 16;

    /// <summary>Согласованный набор шифров.</summary>
    public CipherSuite NegotiatedCipherSuite { get; private set; }

    /// <summary>Группа обмена ключами, выбранная сервером.</summary>
    public NamedGroup NegotiatedGroup { get; private set; }

    /// <summary>Протокол, выбранный сервером в ALPN.</summary>
    public string? NegotiatedProtocol { get; private set; }

    /// <summary>
    /// Номер расширения application_settings, если сервер согласовал ALPS; иначе ноль.
    /// </summary>
    public ushort NegotiatedApplicationSettings { get; private set; }

    /// <summary>
    /// Параметры транспорта QUIC, присланные сервером, в сыром виде.
    /// </summary>
    /// <remarks>
    /// TLS их только переносит: смысл полей принадлежит транспорту QUIC, и разбирать их здесь
    /// значило бы поселить знание о QUIC внутри TLS. Пусто для обычных соединений поверх TCP.
    /// </remarks>
    public ReadOnlyMemory<byte> PeerQuicTransportParameters { get; private set; }

    /// <summary>Цепочка сертификатов сервера в порядке получения.</summary>
    public IReadOnlyList<X509Certificate2> ServerCertificates => serverCertificates;

    /// <summary>Секрет трафика рукопожатия клиента.</summary>
    public ReadOnlyMemory<byte> ClientHandshakeSecret { get; private set; }

    /// <summary>Секрет трафика рукопожатия сервера.</summary>
    public ReadOnlyMemory<byte> ServerHandshakeSecret { get; private set; }

    /// <summary>Прикладной секрет трафика клиента; появляется после <see cref="DeriveApplicationSecrets"/>.</summary>
    public ReadOnlyMemory<byte> ClientApplicationSecret { get; private set; }

    /// <summary>Прикладной секрет трафика сервера; появляется после <see cref="DeriveApplicationSecrets"/>.</summary>
    public ReadOnlyMemory<byte> ServerApplicationSecret { get; private set; }

    /// <summary>Сервер принял предложенный PSK-идентификатор — рукопожатие идёт через возобновление.</summary>
    /// <remarks>
    /// Сигнал — расширение pre_shared_key в ServerHello (RFC 8446, §4.2.11): сервер не повторяет
    /// его при полном рукопожатии. Без этого бита ранний секрет считался бы вслепую, а возобновление
    /// с отвергнутым билетом обрывалось бы на первой расшифровке.
    /// </remarks>
    public bool ResumptionAccepted => resumptionAccepted;

    /// <summary>
    /// Resumption master secret; появляется после <see cref="CaptureResumptionMasterSecret"/>.
    /// </summary>
    public ReadOnlyMemory<byte> ResumptionMasterSecret { get; private set; }

    /// <summary>Предложение возобновления; <see langword="null"/>, когда рукопожатие полное.</summary>
    private readonly Tls13PskOffer? pskOffer = settings.PskOffer;

    /// <summary>Master secret, живой до конца рукопожатия — из него выводится resumption secret.</summary>
    private byte[]? masterSecret;

    /// <summary>Выбрал ли сервер наш PSK-идентификатор.</summary>
    private bool resumptionAccepted;

    /// <summary>
    /// Принимает EndOfEarlyData в транскрипт.
    /// </summary>
    /// <remarks>
    /// Сообщение не несёт содержимого, но сервер включает его в хэш транскрипта при проверке
    /// Finished клиента, поэтому и мы обязаны его туда положить после отправки.
    /// </remarks>
    public void AppendEndOfEarlyData(ReadOnlySpan<byte> message) => transcript.Append(message);

    /// <summary>Ранний секрет трафика клиента для 0-RTT; пусто, когда 0-RTT не предлагался.</summary>
    public ReadOnlyMemory<byte> ClientEarlyTrafficSecret { get; private set; }

    /// <summary>Предлагался ли 0-RTT в этом приветствии.</summary>
    public bool EarlyDataOffered { get; private set; }

    /// <summary>Сервер подтвердил приём 0-RTT (early_data в EncryptedExtensions).</summary>
    /// <remarks>
    /// Если подтверждения нет, отправленные ранние данные сервер проигнорировал, и вызывающая
    /// сторона обязана переотправить запрос уже под согласованными ключами.
    /// </remarks>
    public bool EarlyDataAccepted { get; private set; }

    /// <summary>
    /// Строит ClientHello и возвращает его как сообщение рукопожатия, без заголовка записи.
    /// </summary>
    /// <returns>Сообщение рукопожатия целиком, включая четырёхбайтовый заголовок.</returns>
    /// <remarks>
    /// Сообщение сразу попадает в транскрипт: он считается по сообщениям рукопожатия, а не по
    /// записям, и смешение этих уровней ломает проверку Finished.
    /// </remarks>
    public byte[] BuildClientHello()
    {
        // Второе приветствие отличается от первого ровно двумя вещами: долей ключа для группы,
        // которую назвал сервер, и эхом его cookie. Всё прочее — состав, порядок, случайное
        // число сессии — обязано остаться прежним, иначе сервер сочтёт сообщение чужим.
        var extensions = retryGroup is null ? WithPskExtensions(OrderedExtensions) : BuildRetryExtensions();

        var builder = ClientHelloBuilder.Create()
            .WithCipherSuites(Settings.CipherSuites)
            .WithExtensions(extensions)
            .WithSessionIdPolicy(Settings.SessionIdPolicy);

        // Второе приветствие повторяет первое: те же случайное число и идентификатор сессии.
        if (helloRandom is not null) builder.WithFixedIdentity(helloRandom, helloSessionId ?? []);

        try
        {
            var record = builder.Build();

            // Построитель отдаёт готовую запись; уровню сообщений нужен её пятибайтовый вычет.
            var recordLength = (record[3] << 8) | record[4];
            var message = record.Slice(5, recordLength).ToArray();

            // Binder обязан попасть в транскрипт вместе с приветствием: он считается по
            // усечённому ClientHello, но в транскрипте идёт ПОЛНОЕ сообщение.
            if (pskOffer is not null && helloRandom is null) ApplyPskBinder(message);

            EarlyDataOffered = pskOffer is not null && pskOffer.MaxEarlyData > 0 && helloRandom is null;

            RememberHelloIdentity(message);

            transcript.Append(message);

            // Ранний секрет 0-RTT считается ровно по ClientHello: это ЕДИНСТВЕННОЕ сообщение
            // транскрипта на момент отправки ранних данных (RFC 8446, §7.1).
            if (EarlyDataOffered)
            {
                var earlySecret = Tls13KeySchedule.DeriveEarlySecret(pskOffer!.Hash, pskOffer.PreSharedKey.Span);
                ClientEarlyTrafficSecret = Tls13KeySchedule.DeriveSecret(pskOffer.Hash, earlySecret, "c e traffic", transcript.ComputeHash(pskOffer.Hash));
            }
            NeedsRetryPending = false;
            sniHost = ReadServerName(extensions);
            clientKeyShares = ReadClientKeyShares(extensions);

            return message;
        }
        finally
        {
            ClientHelloBuilder.Return(builder);
        }
    }

    /// <summary>
    /// Запоминает случайное число и идентификатор сессии первого приветствия.
    /// </summary>
    /// <param name="message">Сообщение ClientHello целиком.</param>
    /// <remarks>
    /// ★ Повторное приветствие после HelloRetryRequest обязано совпадать с первым во всём, кроме
    /// доли ключа и эха cookie (RFC 8446, §4.1.2). Прежде оно строилось со свежими значениями —
    /// то есть было для сервера ДРУГИМ сообщением; строгая проверка вправе оборвать такое
    /// соединение как нарушение протокола. Заодно от случайного числа выводятся подставные
    /// значения GREASE, и они разъезжались вместе с ним.
    /// </remarks>
    private void RememberHelloIdentity(ReadOnlySpan<byte> message)
    {
        if (helloRandom is not null || message.Length < 39) return;

        helloRandom = message.Slice(6, 32).ToArray();

        var sessionIdLength = message[38];
        if (message.Length >= 39 + sessionIdLength) helloSessionId = message.Slice(39, sessionIdLength).ToArray();
    }

    /// <summary>
    /// Дополняет расширения первого приветствия предложением возобновления сессии.
    /// </summary>
    /// <param name="extensions">Расширения в порядке ухода на провод.</param>
    /// <returns>Расширения с psk_key_exchange_modes и замыкающим pre_shared_key.</returns>
    /// <remarks>
    /// PSK строится заново из <see cref="TlsSettings.PskOffer"/>, а не берётся из списка:
    /// obfuscated ticket age зависит от текущего времени, а binder подставляется позже, когда
    /// сообщение собрано. Расширение pre_shared_key обязано замыкать ClientHello — перестановка
    /// его не двигает, но и мы держим его последним на случай ручных настроек без перестановки.
    /// </remarks>
    private IEnumerable<Extensions.ITlsExtension> WithPskExtensions(IEnumerable<Extensions.ITlsExtension> extensions)
    {
        if (pskOffer is null) return extensions;

        var hasPskKeyExchangeModes = false;
        var result = new List<Extensions.ITlsExtension>();

        foreach (var extension in extensions)
        {
            // Старое предложение выбрасывается: его возраст и binder заведомо устарели.
            if (extension is Extensions.PreSharedKeyTlsExtension) continue;

            if (extension is Extensions.PskKeyExchangeModesTlsExtension) hasPskKeyExchangeModes = true;
            result.Add(extension);
        }

        if (pskOffer.MaxEarlyData > 0) result.Add(new Extensions.EarlyDataTlsExtension());

        if (!hasPskKeyExchangeModes)
        {
            // ★ Modes присваивается ЯВНО: длину поля расширение считает в сеттере, и полагаться
            // на свойство-инициализатор по умолчанию здесь нельзя.
            result.Add(new Extensions.PskKeyExchangeModesTlsExtension { Modes = [Extensions.PskKeyExchangeMode.PskDheKe] });
        }

        result.Add(new Extensions.PreSharedKeyTlsExtension
        {
            Identities = [new Extensions.PskIdentity
            {
                Identity = pskOffer.Identity,
                ObfuscatedTicketAge = pskOffer.ObfuscatedTicketAge,
            }],

            // Placeholder нужной длины: значения binders подставит ApplyPskBinder по собранному
            // сообщению — считать их до сборки нечем, хэш зависит от всего приветствия.
            Binders = [new byte[pskOffer.BinderLength]],
        });

        return result;
    }

    /// <summary>
    /// Вычисляет и подставляет binder в собранное первое приветствие с PSK.
    /// </summary>
    /// <param name="message">Сообщение ClientHello целиком, ещё не попавшее в транскрипт.</param>
    /// <remarks>
    /// Binder — HMAC по хэшу транскрипта с усечённым ClientHello (RFC 8446, §4.2.11.2). Усечённым
    /// считается приветствие БЕЗ самих значений binder'ов: их однобайтовые длины и длина вектора
    /// остаются в хэше. Транскрипт до первого приветствия пуст, поэтому хэш считается
    /// по одному сообщению; при HelloRetryRequest предложение PSK снимается — пересчёт binder'а
    /// по частично заменённому транскрипту здесь не поддерживается.
    /// </remarks>
    private void ApplyPskBinder(byte[] message)
    {
        var hashLength = pskOffer!.BinderLength;
        var truncatedLength = message.Length - (2 + 1 + hashLength);

        // Проверка макета: усечённое приветствие обязано кончаться вектором binders — двухбайтовая
        // длина, затем однобайтовая длина записи, равная размеру хэша билета.
        if (truncatedLength <= 4 || message[truncatedLength + 2] != hashLength)
            throw new InvalidOperationException("Расширение pre_shared_key не замыкает ClientHello");

        var early = Tls13KeySchedule.DeriveEarlySecret(pskOffer.Hash, pskOffer.PreSharedKey.Span);
        var binderKey = Tls13KeySchedule.DeriveBinderKey(pskOffer.Hash, early, pskOffer.External);

        // Binder = HMAC(finished_key, хэш усечённого приветствия), где finished_key выводится из
        // binder_key меткой "finished" — ровно как verify_data в Finished (RFC 8446, §4.2.11.2).
        var finishedKey = Tls13KeySchedule.DeriveFinishedKey(pskOffer.Hash, binderKey);
        var truncatedHash = pskOffer.Hash == HashAlgorithmName.SHA384
            ? SHA384.HashData(message.AsSpan(0, truncatedLength))
            : SHA256.HashData(message.AsSpan(0, truncatedLength));
        var binder = Tls13KeySchedule.ComputePskBinder(pskOffer.Hash, finishedKey, truncatedHash);

        binder.CopyTo(message, truncatedLength + 3);
    }

    /// <summary>
    /// Принимает ServerHello и выводит секреты рукопожатия.
    /// </summary>
    /// <param name="message">Сообщение целиком, включая четырёхбайтовый заголовок.</param>
    public void ProcessServerHello(ReadOnlySpan<byte> message)
    {
        if (message.Length < 4 || message[0] != (byte)TlsHandshakeType.ServerHello)
            throw new InvalidOperationException("Ожидался ServerHello");

        var body = message[4..];
        var isRetry = body.Length >= 2 + 32 && body.Slice(2, 32).SequenceEqual(HelloRetryRequestRandom);

        // ★ При HelloRetryRequest первое приветствие клиента заменяется в транскрипте
        // синтетическим сообщением с его хэшем (RFC 8446, §4.4.1), и СТРОГО до того, как в
        // транскрипт попадёт сам HelloRetryRequest. Порядок здесь не формальность: перепутав
        // его, получим неверный verify_data в Finished — то есть отказ в самом конце
        // рукопожатия, успешного во всём остальном.
        if (isRetry)
        {
            ApplyCipherSuite(BinaryPrimitives.ReadUInt16BigEndian(body.Slice(2 + 32 + 1 + body[2 + 32], 2)));
            transcript.ReplaceWithMessageHash(HashAlgorithm);
        }

        transcript.Append(message);
        ParseServerHello(body);

        // После HelloRetryRequest секретов ещё нет: их выведет второе приветствие.
        if (!isRetry) DeriveHandshakeSecrets();
    }

    /// <summary>Сервер попросил повторить приветствие с другой группой, и повтор ещё не отправлен.</summary>
    public bool NeedsRetryPending { get; private set; }

    /// <summary>Сервер попросил повторить приветствие с другой группой.</summary>
    public bool NeedsRetry => NeedsRetryPending;

    /// <summary>
    /// Строит встречное EncryptedExtensions клиента, если сервер согласовал ALPS.
    /// </summary>
    /// <returns>Сообщение рукопожатия либо <see langword="null"/>, если ALPS не согласован.</returns>
    /// <remarks>
    /// ★ Без этого сообщения Google обрывает соединение, и обрывает УЖЕ ПОСЛЕ успешного
    /// рукопожатия: сертификат проверен, ALPN согласован, ключи выведены — и первая же запись
    /// сервера под прикладными ключами оказывается фатальным alert 10 (unexpected_message).
    /// Причина в том, что ALPS двусторонний: подтвердив расширение, сервер ждёт от клиента
    /// собственное EncryptedExtensions ПЕРЕД Finished, и, увидев вместо него Finished, считает
    /// сообщение неожиданным.
    ///
    /// Заметить это тяжело: Cloudflare ALPS не поддерживает и потому работает без нареканий, а
    /// профиль Firefox расширения не объявляет вовсе и с Google работает. То есть отказ выглядит
    /// избирательным по серверам и по профилям и ничем не намекает на собственную причину.
    ///
    /// Убрать расширение из ClientHello было бы куда проще, но это прямо меняет ja3 и ja4:
    /// настоящий Chrome ALPS отправляет. Правильный путь — довести протокол до конца.
    ///
    /// Значение настроек пустое. Наблюдать его пассивно нельзя — оно едет под ключами
    /// рукопожатия и видно только серверу, — а Google такую форму принимает.
    /// </remarks>
    public byte[]? BuildClientEncryptedExtensions()
    {
        if (NegotiatedApplicationSettings is 0) return null;

        // Тело: список расширений (2 байта длины), внутри одно расширение с пустым значением.
        var message = new byte[4 + 2 + 4];

        message[0] = (byte)TlsHandshakeType.EncryptedExtensions;
        message[1] = 0;
        message[2] = 0;
        message[3] = 6;

        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(4, 2), 4);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(6, 2), NegotiatedApplicationSettings);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(8, 2), 0);

        // В транскрипт — ОБЯЗАТЕЛЬНО здесь: verify_data в Finished считается по транскрипту,
        // и сообщение, отправленное раньше Finished, обязано быть учтено раньше него.
        transcript.Append(message);

        return message;
    }

    /// <summary>
    /// Строит ответ на запрос сертификата клиента, если сервер его прислал.
    /// </summary>
    /// <returns>Сообщение Certificate с пустым списком либо <see langword="null"/>.</returns>
    /// <remarks>
    /// ★ Сервер вправе попросить сертификат клиента и на обычном публичном сайте — это
    /// НЕОБЯЗАТЕЛЬНАЯ проверка, и браузер, у которого подходящего сертификата нет, отвечает
    /// ПУСТЫМ списком и спокойно продолжает (RFC 8446, §4.4.2: «клиент ОБЯЗАН отправить
    /// Certificate, даже если он пуст»).
    ///
    /// Мы же запрос игнорировали и слали сразу Finished. Сервер ждал Certificate, получал
    /// Finished и обрывал соединение фатальным <c>unexpected_message</c> — уже ПОСЛЕ успешной
    /// проверки его собственного сертификата, то есть выглядело это как поломка на ровном месте.
    /// Замер на двух сотнях узлов: так были полностью недоступны <c>www.bund.de</c> (портал
    /// правительства Германии) и <c>www.zomato.com</c>, на всех трёх профилях сразу.
    ///
    /// Контекст копируется из запроса дословно: сервер сверяет его с тем, что прислал сам.
    ///
    /// CertificateVerify при пустом списке НЕ отправляется — подписывать нечем, и спецификация
    /// его в этом случае не требует.
    /// </remarks>
    public byte[]? BuildClientCertificate()
    {
        if (certificateRequestContext is null) return null;

        var context = certificateRequestContext;
        var body = 1 + context.Length + 3;
        var message = new byte[4 + body];

        message[0] = (byte)TlsHandshakeType.Certificate;
        message[1] = (byte)(body >> 16);
        message[2] = (byte)(body >> 8);
        message[3] = (byte)body;

        message[4] = (byte)context.Length;
        context.CopyTo(message.AsSpan(5));

        // Длина списка сертификатов — три нулевых байта: список пуст.

        transcript.Append(message);

        return message;
    }

    /// <summary>
    /// Запоминает контекст запроса сертификата клиента.
    /// </summary>
    /// <param name="body">Тело сообщения CertificateRequest.</param>
    private void ParseCertificateRequest(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty) return;

        var length = body[0];
        if (1 + length > body.Length) throw new InvalidOperationException("Испорченный CertificateRequest");

        certificateRequestContext = body.Slice(1, length).ToArray();
    }

    /// <summary>
    /// Строит сообщение Finished клиента и добавляет его в транскрипт.
    /// </summary>
    /// <returns>Сообщение рукопожатия целиком.</returns>
    public byte[] BuildClientFinished()
    {
        if (ClientHandshakeSecret.IsEmpty) throw new InvalidOperationException("Нет клиентского секрета рукопожатия");

        var finishedKey = Tls13KeySchedule.DeriveFinishedKey(HashAlgorithm, ClientHandshakeSecret.Span);
        var verifyData = Tls13KeySchedule.ComputeVerifyData(HashAlgorithm, finishedKey, transcript.ComputeHash(HashAlgorithm));

        var message = new byte[4 + verifyData.Length];
        message[0] = (byte)TlsHandshakeType.Finished;
        message[1] = 0;
        message[2] = (byte)(verifyData.Length >> 8);
        message[3] = (byte)verifyData.Length;
        verifyData.CopyTo(message, 4);

        transcript.Append(message);
        return message;
    }

    /// <summary>
    /// Выводит прикладные секреты трафика.
    /// </summary>
    /// <remarks>
    /// Считаются из транскрипта ДО Finished сервера включительно и БЕЗ Finished клиента. Снимок
    /// берётся в момент разбора Finished сервера: посчитав хэш позже, мы разошлись бы с серверными
    /// ключами — молча, с виду как сетевой сбой.
    /// </remarks>
    public void DeriveApplicationSecrets()
    {
        if (handshakeSecret is null) throw new InvalidOperationException("Нет секрета рукопожатия");

        var master = Tls13KeySchedule.DeriveMasterSecret(HashAlgorithm, handshakeSecret);
        masterSecret = master;
        var transcriptHash = serverFinishedTranscriptHash ?? throw new InvalidOperationException("Нет снимка транскрипта на момент Finished сервера");

        ClientApplicationSecret = Tls13KeySchedule.DeriveSecret(HashAlgorithm, master, "c ap traffic", transcriptHash);
        ServerApplicationSecret = Tls13KeySchedule.DeriveSecret(HashAlgorithm, master, "s ap traffic", transcriptHash);
    }

    /// <summary>
    /// Выводит resumption master secret — на его основе считаются PSK билетов сессии.
    /// </summary>
    /// <remarks>
    /// Вызывается ПОСЛЕ отправки Finished клиента и ДО освобождения транскрипта: по RFC 8446
    /// (§7.1) входной хэш снимается по транскрипту, включающему Finished клиента. Успеть надо
    /// до <see cref="ReleaseTranscript"/> — позже транскрипт уже опустошён, и секрет уходит
    /// недоступным навсегда вместе с возможностью возобновлять сессию.
    /// </remarks>
    public void CaptureResumptionMasterSecret()
    {
        if (masterSecret is null || !ResumptionMasterSecret.IsEmpty) return;

        var transcriptHash = transcript.ComputeHash(HashAlgorithm);
        ResumptionMasterSecret = Tls13KeySchedule.DeriveResumptionMasterSecret(HashAlgorithm, masterSecret, transcriptHash);
    }

    /// <summary>
    /// Выводит секреты рукопожатия из общего секрета и текущего транскрипта.
    /// </summary>
    private void DeriveHandshakeSecrets()
    {
        if (sharedSecret is null) throw new InvalidOperationException("Общий секрет не выведен");

        var transcriptHash = transcript.ComputeHash(HashAlgorithm);

        // PSK входит в ранний секрет ТОЛЬКО когда сервер подтвердил выбор identity: иначе он
        // выводил ключи без PSK, и любое расхождение здесь стало бы провалом расшифровки полёта.
        var early = Tls13KeySchedule.DeriveEarlySecret(HashAlgorithm, resumptionAccepted ? pskOffer!.PreSharedKey.Span : []);
        handshakeSecret = Tls13KeySchedule.DeriveHandshakeSecret(HashAlgorithm, early, sharedSecret);

        ClientHandshakeSecret = Tls13KeySchedule.DeriveSecret(HashAlgorithm, handshakeSecret, "c hs traffic", transcriptHash);
        ServerHandshakeSecret = Tls13KeySchedule.DeriveSecret(HashAlgorithm, handshakeSecret, "s hs traffic", transcriptHash);
    }

    private void ParseServerHello(ReadOnlySpan<byte> body)
    {
        // legacy_version(2) + random(32) + session_id(1+n) + cipher_suite(2) + compression(1)
        var position = 2 + 32;
        if (body.Length < position + 1) throw new InvalidOperationException("Короткий ServerHello");

        var sessionIdLength = body[position++];
        position += sessionIdLength;
        if (body.Length < position + 3) throw new InvalidOperationException("Короткий ServerHello");

        var suite = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(position, 2));
        position += 2;
        position += 1;

        if (body.Length < position + 2) throw new InvalidOperationException("ServerHello без расширений");
        var extensionsLength = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(position, 2));
        position += 2;

        var extensions = body.Slice(position, extensionsLength);

        // HelloRetryRequest — это ServerHello с особым, заданным спецификацией значением random
        // (RFC 8446, §4.1.3). Отличить его иначе нельзя: тип сообщения у них общий.
        if (body.Length >= 2 + 32 && body.Slice(2, 32).SequenceEqual(HelloRetryRequestRandom))
        {
            ApplyCipherSuite(suite);
            PrepareRetry(extensions);

            return;
        }

        // ★ Версию объявляет расширение supported_versions, а НЕ поле legacy_version: в TLS 1.3
        // последнее навсегда равно 0x0303 ради совместимости с промежуточным оборудованием.
        // Отсутствие расширения означает, что сервер выбрал TLS 1.2, и разбирать остальное по
        // правилам 1.3 бессмысленно — до этой проверки всё падало на наборе шифров из 1.2 с
        // сообщением, которое причину не называло.
        if (!ServerChoseTls13(extensions)) throw new TlsVersionDowngradeException();

        ApplyCipherSuite(suite);

        // ★ Принятие PSK видно УЖЕ в ServerHello: сервер отвечает расширением pre_shared_key
        // с индексом выбранного идентификатора (RFC 8446, §4.2.11). Без него билет отвергнут,
        // ранний секрет считается с нулевым PSK, и рукопожатие идёт как полное — предложенный
        // билет на это не влияет.
        resumptionAccepted = pskOffer is not null && ServerSelectedPskIdentity(extensions);

        var serverPublicKey = ReadServerKeyShare(extensions);
        if (serverPublicKey.IsEmpty) throw new InvalidOperationException("ServerHello без key_share");

        sharedSecret = ComputeSharedSecret(serverPublicKey);
    }

    /// <summary>
    /// Проверяет, выбрал ли сервер наш PSK-идентификатор.
    /// </summary>
    /// <param name="extensions">Расширения ServerHello.</param>
    /// <returns><see langword="true"/>, если в ServerHello есть pre_shared_key.</returns>
    private static bool ServerSelectedPskIdentity(ReadOnlySpan<byte> extensions)
    {
        var position = 0;

        while (position + 4 <= extensions.Length)
        {
            var id = BinaryPrimitives.ReadUInt16BigEndian(extensions.Slice(position, 2));
            var length = BinaryPrimitives.ReadUInt16BigEndian(extensions.Slice(position + 2, 2));
            position += 4;

            if (id is 0x0029 && length >= 2) return true;

            position += length;
        }

        return false;
    }

    private NamedGroup? retryGroup;
    private byte[]? retryCookie;

    /// <summary>
    /// Запоминает, чего потребовал сервер в HelloRetryRequest.
    /// </summary>
    /// <param name="extensions">Расширения сообщения.</param>
    /// <remarks>
    /// Повтор допускается ровно ОДИН раз (RFC 8446, §4.1.4): второй подряд означает, что сервер
    /// либо неисправен, либо водит нас по кругу, и продолжать бессмысленно.
    ///
    /// Группа, которую он назвал, обязана быть из числа объявленных нами — иначе это ответ не на
    /// наше приветствие. Cookie, если он есть, отправляется обратно без изменений: сервер кладёт
    /// в него собственное состояние, чтобы не хранить его у себя.
    /// </remarks>
    private void PrepareRetry(ReadOnlySpan<byte> extensions)
    {
        if (retryGroup is not null) throw new InvalidOperationException("Сервер прислал HelloRetryRequest дважды");

        NamedGroup? requested = null;
        var position = 0;

        while (position + 4 <= extensions.Length)
        {
            var id = BinaryPrimitives.ReadUInt16BigEndian(extensions.Slice(position, 2));
            var length = BinaryPrimitives.ReadUInt16BigEndian(extensions.Slice(position + 2, 2));
            position += 4;

            if (position + length > extensions.Length) break;

            // В HelloRetryRequest key_share несёт ТОЛЬКО номер группы, без самой доли.
            if (id is 0x0033 && length >= 2)
                requested = (NamedGroup)BinaryPrimitives.ReadUInt16BigEndian(extensions.Slice(position, 2));

            if (id is 0x002C && length > 0) retryCookie = extensions.Slice(position, length).ToArray();

            position += length;
        }

        if (requested is not { } group) throw new InvalidOperationException("HelloRetryRequest без указания группы");

        if (!IsOfferedGroup(group))
            throw new InvalidOperationException($"Сервер требует группу {group} (0x{(ushort)group:X4}), которой мы не предлагали");

        retryGroup = group;
        NeedsRetryPending = true;
    }

    /// <summary>
    /// Проверяет, объявляли ли мы эту группу в supported_groups.
    /// </summary>
    /// <param name="group">Группа из HelloRetryRequest.</param>
    /// <returns><see langword="true"/>, если группа была предложена.</returns>
    private bool IsOfferedGroup(NamedGroup group)
    {
        foreach (var extension in Settings.Extensions)
        {
            if (extension is not Extensions.SupportedGroupsTlsExtension supported) continue;

            foreach (var offered in supported.Groups)
            {
                if (offered == group) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Собирает расширения второго приветствия.
    /// </summary>
    /// <returns>Расширения с новой долей ключа и эхом cookie.</returns>
    /// <remarks>
    /// Порядок сохраняется полностью: меняется содержимое key_share, а cookie добавляется в
    /// конец — так его ставит и браузер. Всё остальное переносится как есть, потому что второе
    /// приветствие обязано отличаться от первого ТОЛЬКО тем, о чём попросил сервер.
    /// </remarks>
    private Extensions.ITlsExtension[] BuildRetryExtensions()
    {
        var share = KeyShare.ForGroup(retryGroup!.Value);
        var result = new List<Extensions.ITlsExtension>(OrderedExtensions.Count() + 1);

        foreach (var extension in OrderedExtensions)
        {
            // Предложение PSK снимается: binder обязан пересчитываться по частично заменённому
            // транскрипту после HelloRetryRequest, что здесь не поддерживается. Соединение
            // продолжится полным рукопожатием — ровно как у сервера, отвергшего билет.
            if (extension is Extensions.PreSharedKeyTlsExtension) continue;

            result.Add(extension is Extensions.KeyShareTlsExtension keyShare
                ? new Extensions.KeyShareTlsExtension { Id = keyShare.Id, Entries = [share] }
                : extension);
        }

        if (retryCookie is { Length: > 0 }) result.Add(new Extensions.CookieTlsExtension { Cookie = retryCookie });

        return [.. result];
    }

    /// <summary>
    /// Значение random, которым сервер помечает HelloRetryRequest (RFC 8446, §4.1.3).
    /// </summary>
    private static ReadOnlySpan<byte> HelloRetryRequestRandom =>
    [
        0xCF, 0x21, 0xAD, 0x74, 0xE5, 0x9A, 0x61, 0x11, 0xBE, 0x1D, 0x8C, 0x02, 0x1E, 0x65, 0xB8, 0x91,
        0xC2, 0xA2, 0x11, 0x16, 0x7A, 0xBB, 0x8C, 0x5E, 0x07, 0x9E, 0x09, 0xE2, 0xC8, 0xA8, 0x33, 0x9C,
    ];

    /// <summary>
    /// Определяет, выбрал ли сервер TLS 1.3.
    /// </summary>
    /// <param name="extensions">Расширения ServerHello.</param>
    /// <returns><see langword="true"/>, если согласован TLS 1.3.</returns>
    private static bool ServerChoseTls13(ReadOnlySpan<byte> extensions)
    {
        var position = 0;

        while (position + 4 <= extensions.Length)
        {
            var id = BinaryPrimitives.ReadUInt16BigEndian(extensions.Slice(position, 2));
            var length = BinaryPrimitives.ReadUInt16BigEndian(extensions.Slice(position + 2, 2));
            position += 4;

            if (position + length > extensions.Length) break;

            // supported_versions в ServerHello несёт ровно одну версию — выбранную.
            if (id is 0x002B && length >= 2)
                return BinaryPrimitives.ReadUInt16BigEndian(extensions.Slice(position, 2)) is 0x0304;

            position += length;
        }

        return false;
    }

    /// <summary>
    /// Применяет выбранный сервером набор шифров.
    /// </summary>
    /// <param name="suite">Номер набора.</param>
    /// <remarks>
    /// ★ ChaCha20 здесь обязателен, а не желателен. Мы объявляем его в ClientHello, и на машинах
    /// БЕЗ аппаратного ускорения AES профиль ставит его ПЕРВЫМ — то есть именно там сервер его и
    /// выберет. Не поддержав выбранный набор, клиент обрывает соединение сразу после ServerHello:
    /// на обычном оборудовании всё работает, на слабом не работает ничего, и причина выглядит
    /// сетевой. Убрать набор из ClientHello нельзя — это прямо меняет ja3 и ja4.
    /// </remarks>
    private void ApplyCipherSuite(ushort suite)
    {
        NegotiatedCipherSuite = (CipherSuite)suite;

        (HashAlgorithm, AeadKeyLength) = (CipherSuite)suite switch
        {
            CipherSuite.TLS_AES_128_GCM_SHA256 => (HashAlgorithmName.SHA256, 16),
            CipherSuite.TLS_AES_256_GCM_SHA384 => (HashAlgorithmName.SHA384, 32),
            CipherSuite.TLS_CHACHA20_POLY1305_SHA256 => ChaCha20Poly1305Aead.IsSupported
                ? (HashAlgorithmName.SHA256, 32)
                : throw new NotSupportedException("Сервер выбрал ChaCha20-Poly1305, но платформенный примитив недоступен"),
            _ => throw new NotSupportedException($"Набор шифров 0x{suite:X4} не поддержан в TLS 1.3"),
        };
    }

    private ReadOnlySpan<byte> ReadServerKeyShare(ReadOnlySpan<byte> extensions)
    {
        var position = 0;

        while (position + 4 <= extensions.Length)
        {
            var id = BinaryPrimitives.ReadUInt16BigEndian(extensions.Slice(position, 2));
            var length = BinaryPrimitives.ReadUInt16BigEndian(extensions.Slice(position + 2, 2));
            position += 4;

            if (position + length > extensions.Length) break;

            // key_share (0x0033): group(2) + key_exchange<1..2^16-1>
            if (id is 0x0033 && length >= 4)
            {
                var body = extensions.Slice(position, length);
                var keyLength = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(2, 2));

                if (4 + keyLength <= body.Length)
                {
                    // Группу сервер называет явно: только она и определяет, какой из наших
                    // эфемерных ключей нужен для вывода общего секрета.
                    NegotiatedGroup = (NamedGroup)BinaryPrimitives.ReadUInt16BigEndian(body[..2]);
                    return body.Slice(4, keyLength);
                }
            }

            position += length;
        }

        return [];
    }

    private byte[] ComputeSharedSecret(ReadOnlySpan<byte> serverPublicKey)
    {
        clientKeyShare = SelectClientKeyShare();

        if (clientKeyShare is null) throw new InvalidOperationException("Не задан клиентский key_share");

        return clientKeyShare.Group switch
        {
            NamedGroup.X25519 => DeriveX25519(serverPublicKey),
            NamedGroup.X25519MLKem768 => DeriveHybrid(serverPublicKey),
            NamedGroup.Secp256r1 or NamedGroup.Secp384r1 or NamedGroup.Secp521r1 => DeriveEcdh(serverPublicKey),
            _ => throw new NotSupportedException($"Группа {clientKeyShare.Group} не поддержана"),
        };
    }

    /// <summary>
    /// Выбирает долю ключа, соответствующую группе, которую назвал сервер.
    /// </summary>
    /// <returns>Подходящая доля либо первая отправленная, если группа неизвестна.</returns>
    private KeyShare? SelectClientKeyShare()
    {
        foreach (var share in clientKeyShares)
        {
            if (share.Group == NegotiatedGroup) return share;
        }

        // Сервер обязан назвать одну из предложенных групп. Возврат к первой оставлен для
        // совместимости с путями, где расширение key_share сервера отсутствует.
        return clientKeyShares.Count > 0 ? clientKeyShares[0] : null;
    }

    /// <summary>
    /// Выводит общий секрет гибрида X25519 + ML-KEM-768.
    /// </summary>
    /// <param name="serverShare">Доля ключа сервера: шифротекст ML-KEM, затем открытый ключ X25519.</param>
    /// <returns>Конкатенация секрета ML-KEM и секрета X25519.</returns>
    /// <remarks>
    /// Порядок обеих склеек задан черновиком draft-kwiatkowski-tls-ecdhe-mlkem и обратен
    /// привычному: постквантовая часть идёт ПЕРВОЙ и в доле ключа, и в общем секрете. Перепутанный
    /// порядок даёт неверный секрет, а проявляется это провалом расшифровки первой же записи —
    /// то есть выглядит как сетевой сбой.
    /// </remarks>
    private byte[] DeriveHybrid(ReadOnlySpan<byte> serverShare)
    {
        if (clientKeyShare?.Ephemeral is not HybridKeyMaterial material)
            throw new InvalidOperationException("Не найден секрет гибридного ключа");

        const int CiphertextLength = 1088;
        const int X25519PublicKeyLength = 32;

        if (serverShare.Length != CiphertextLength + X25519PublicKeyLength)
            throw new InvalidOperationException($"Доля ключа сервера для X25519MLKEM768 имеет длину {serverShare.Length} вместо {CiphertextLength + X25519PublicKeyLength}");

        var kemSecret = new byte[32];
        material.Decapsulate(serverShare[..CiphertextLength], kemSecret);

        var classicSecret = material.DeriveX25519(serverShare[CiphertextLength..]);

        var shared = new byte[kemSecret.Length + classicSecret.Length];
        kemSecret.CopyTo(shared.AsSpan());
        classicSecret.CopyTo(shared.AsSpan(kemSecret.Length));

        CryptographicOperations.ZeroMemory(kemSecret);
        CryptographicOperations.ZeroMemory(classicSecret);

        return shared;
    }

    private byte[] DeriveX25519(ReadOnlySpan<byte> serverPublicKey)
    {
        // Приватный скаляр X25519 хранится как массив байт: платформенный объект ключа для этой
        // кривой недоступен, и собственная реализация в нём не нуждается.
        if (clientKeyShare?.Ephemeral is not byte[] privateKey) throw new InvalidOperationException("Не найден приватный ключ X25519");

        return Tls.X25519.DeriveSharedSecret(privateKey, serverPublicKey);
    }

    private byte[] DeriveEcdh(ReadOnlySpan<byte> serverPublicKey)
    {
        if (clientKeyShare?.Ephemeral is not ECDiffieHellman ecdh) throw new InvalidOperationException("Эфемерный ключ недоступен");

        // Размер координаты задан кривой: 32 байта у P-256, 48 у P-384 и 66 у P-521 — у последней
        // порядок поля 521 бит, то есть неполные 66 байт, и округление тут вверх.
        var (curve, coordinateSize) = clientKeyShare.Group switch
        {
            NamedGroup.Secp384r1 => (ECCurve.NamedCurves.nistP384, 48),
            NamedGroup.Secp521r1 => (ECCurve.NamedCurves.nistP521, 66),
            _ => (ECCurve.NamedCurves.nistP256, 32),
        };

        if (serverPublicKey.Length != 1 + (coordinateSize * 2) || serverPublicKey[0] != 0x04)
            throw new InvalidOperationException("Ожидается несжатая точка сервера");

        using var peer = ECDiffieHellman.Create(new ECParameters
        {
            Curve = curve,
            Q = new ECPoint
            {
                X = serverPublicKey.Slice(1, coordinateSize).ToArray(),
                Y = serverPublicKey.Slice(1 + coordinateSize, coordinateSize).ToArray(),
            },
        });

        return ecdh.DeriveRawSecretAgreement(peer.PublicKey);
    }

    /// <summary>
    /// Разбирает сообщения зашифрованного полёта сервера.
    /// </summary>
    /// <param name="data">Очередной кусок полёта: часть сообщения, целое сообщение или несколько подряд.</param>
    /// <returns><see langword="true"/>, когда получен и проверен Finished сервера.</returns>
    /// <remarks>
    /// ★ Границы записей и границы сообщений в TLS 1.3 НЕ СВЯЗАНЫ (RFC 8446, §5.1): одна запись
    /// вправе нести несколько сообщений, а одно сообщение — растянуться на несколько записей.
    /// Прежде каждая запись разбиралась сама по себе, и разрезанное сообщение давало отказ
    /// «оборванное сообщение рукопожатия».
    ///
    /// Проявлялось это далеко не везде: у большинства узлов цепочка сертификатов укладывается в
    /// одну запись, и рукопожатие проходило. Но у Facebook и Instagram она длиннее — и оба узла
    /// были НЕДОСТУПНЫ полностью, на всех трёх профилях. Обычный набор проверок этого не ловил:
    /// он ходит на Cloudflare и Google, где цепочка короткая.
    ///
    /// Здесь же собираются и хвосты: кусок дописывается к остатку предыдущего, разбирается всё
    /// целое, недостающее остаётся ждать следующей записи.
    /// </remarks>
    public bool ProcessHandshakeMessages(ReadOnlySpan<byte> data)
    {
        if (pendingLength is 0)
        {
            var consumed = ProcessCompleteMessages(data, out var completed);
            if (completed) return true;

            RetainTail(data[consumed..]);

            return false;
        }

        AppendToPending(data);

        var processed = ProcessCompleteMessages(pending.AsSpan(0, pendingLength), out var finished);
        if (finished)
        {
            pendingLength = 0;
            return true;
        }

        // Разобранное убираем, остаток сдвигаем к началу: полётов немного и они короткие,
        // поэтому сдвиг дешевле кольцевого буфера и куда понятнее.
        var remaining = pendingLength - processed;
        if (remaining > 0) Array.Copy(pending, processed, pending, 0, remaining);
        pendingLength = remaining;

        return false;
    }

    /// <summary>
    /// Разбирает столько целых сообщений, сколько лежит в буфере.
    /// </summary>
    /// <param name="data">Буфер, начинающийся с границы сообщения.</param>
    /// <param name="finished">Получен ли Finished сервера.</param>
    /// <returns>Число разобранных байт.</returns>
    private int ProcessCompleteMessages(ReadOnlySpan<byte> data, out bool finished)
    {
        var position = 0;
        finished = false;

        while (position + 4 <= data.Length)
        {
            var type = (TlsHandshakeType)data[position];
            var length = (data[position + 1] << 16) | (data[position + 2] << 8) | data[position + 3];
            var total = 4 + length;

            // Сообщение пришло не целиком — оно продолжается в следующей записи.
            if (position + total > data.Length) return position;

            var message = data.Slice(position, total);

            if (type is TlsHandshakeType.Finished)
            {
                // Транскрипт для проверки Finished сервера считается ДО добавления самого Finished.
                VerifyServerFinished(message[4..]);
                transcript.Append(message);

                // Прикладные секреты выводятся из транскрипта ДО Finished сервера ВКЛЮЧИТЕЛЬНО и
                // БЕЗ Finished клиента. Снимок берём здесь: если посчитать хэш позже, после отправки
                // своего Finished, ключи разойдутся с серверными и записи перестанут расшифровываться
                // — причём молча, с виду как сетевой сбой.
                serverFinishedTranscriptHash = transcript.ComputeHash(HashAlgorithm);
                DeriveApplicationSecrets();

                finished = true;

                return position + total;
            }

            if (type is TlsHandshakeType.EncryptedExtensions) ParseEncryptedExtensions(message.Slice(4, length));
            if (type is TlsHandshakeType.CertificateRequest) ParseCertificateRequest(message.Slice(4, length));

            var carriesCertificate = type is TlsHandshakeType.Certificate or TlsHandshakeType.CompressedCertificate;

            if (carriesCertificate)
            {
                if (type is TlsHandshakeType.CompressedCertificate)
                    ParseCompressedCertificate(message.Slice(4, length));
                else
                    ParseCertificate(message.Slice(4, length));

                // Цепочку проверяем сразу: продолжать рукопожатие с неподтверждённым сервером
                // бессмысленно, а ранний отказ экономит и время, и работу над подписью.
                ServerCertificateVerifier.Validate(serverCertificates, sniHost, Settings);
            }

            // В транскрипт идёт сообщение КАК ПОЛУЧЕНО, то есть сжатое (RFC 8879, §5). Подставив
            // распакованный вариант, мы разошлись бы с сервером в хэше и провалили бы проверку
            // подписи CertificateVerify.
            transcript.Append(message);

            // Подпись CertificateVerify считается по транскрипту ВКЛЮЧАЯ Certificate, но НЕ включая
            // саму подпись — поэтому снимок берём сразу после добавления Certificate.
            if (carriesCertificate) certificateTranscriptHash = transcript.ComputeHash(HashAlgorithm);

            if (type is TlsHandshakeType.CertificateVerify) VerifyCertificateSignature(message.Slice(4, length));
            position += total;
        }

        return position;
    }

    /// <summary>
    /// Запоминает неразобранный хвост до следующей записи.
    /// </summary>
    /// <param name="tail">Остаток буфера.</param>
    private void RetainTail(ReadOnlySpan<byte> tail)
    {
        if (tail.IsEmpty) return;

        EnsurePendingCapacity(tail.Length);
        tail.CopyTo(pending);
        pendingLength = tail.Length;
    }

    /// <summary>
    /// Дописывает очередной кусок к накопленному хвосту.
    /// </summary>
    /// <param name="data">Кусок полёта.</param>
    private void AppendToPending(ReadOnlySpan<byte> data)
    {
        EnsurePendingCapacity(pendingLength + data.Length);
        data.CopyTo(pending.AsSpan(pendingLength));
        pendingLength += data.Length;
    }

    /// <summary>
    /// Готовит накопитель нужного размера.
    /// </summary>
    /// <param name="required">Требуемая вместимость.</param>
    /// <remarks>
    /// Предел тот же, что у сообщения Certificate: полёт сервера из целых сообщений длиннее уже
    /// не бывает, а без предела сервер, объявляющий огромную длину и не досылающий данных,
    /// заставил бы буфер расти неограниченно.
    /// </remarks>
    private void EnsurePendingCapacity(int required)
    {
        if (required > MaxCertificateMessageLength)
            throw new InvalidOperationException("Полёт рукопожатия сервера превысил допустимый размер");

        if (pending.Length >= required) return;

        var capacity = Math.Max(required, Math.Max(pending.Length * 2, 4096));
        Array.Resize(ref pending, Math.Min(capacity, MaxCertificateMessageLength));
    }

    /// <summary>
    /// Разбирает EncryptedExtensions и запоминает выбранный сервером протокол ALPN.
    /// </summary>
    /// <remarks>
    /// В TLS 1.3 ответ ALPN приходит уже зашифрованным, а не в ServerHello, как в 1.2. Без этого
    /// разбора вышестоящий слой не узнает, согласован ли HTTP/2, и был бы вынужден гадать — а
    /// несовпадение выбранного протокола с реальным поведением само по себе наблюдаемо.
    /// </remarks>
    private void ParseEncryptedExtensions(ReadOnlySpan<byte> body)
    {
        if (body.Length < 2) return;

        var total = BinaryPrimitives.ReadUInt16BigEndian(body[..2]);
        var extensions = body.Slice(2, Math.Min(total, body.Length - 2));
        var position = 0;

        while (position + 4 <= extensions.Length)
        {
            var id = BinaryPrimitives.ReadUInt16BigEndian(extensions.Slice(position, 2));
            var length = BinaryPrimitives.ReadUInt16BigEndian(extensions.Slice(position + 2, 2));
            position += 4;

            if (position + length > extensions.Length) break;

            // application_layer_protocol_negotiation (0x0010):
            // list<2> + [ len<1> + name ] — сервер возвращает ровно одно имя.
            if (id is 0x0010 && length >= 3)
            {
                var alpn = extensions.Slice(position, length);
                var nameLength = alpn[2];
                if (3 + nameLength <= alpn.Length) NegotiatedProtocol = System.Text.Encoding.ASCII.GetString(alpn.Slice(3, nameLength));
            }

            // quic_transport_parameters (0x0039): переносим наверх как есть.
            if (id is 0x0039 && length > 0) PeerQuicTransportParameters = extensions.Slice(position, length).ToArray();

            // application_settings (ALPS): сервер ПОДТВЕРЖДАЕТ согласование, вернув расширение с
            // тем же номером. Подтвердив, он теперь ЖДЁТ от нас встречное EncryptedExtensions —
            // и это не формальность, см. BuildClientEncryptedExtensions.
            if (id is 0x4469 or 0x44CD) NegotiatedApplicationSettings = id;

            // early_data (0x002A) в EncryptedExtensions — единственное подтверждение, что сервер
            // ПРИНЯЛ наши ранние данные, а не молча выбросил их.
            if (id is 0x002A) EarlyDataAccepted = true;

            position += length;
        }
    }

    /// <summary>
    /// Распаковывает CompressedCertificate и разбирает его как обычный Certificate.
    /// </summary>
    /// <param name="body">Тело сообщения: алгоритм, исходная длина и сжатые данные.</param>
    /// <remarks>
    /// Структура по RFC 8879: алгоритм (2 байта), длина ИСХОДНОГО сообщения (3 байта) и сами
    /// сжатые данные с трёхбайтовой длиной. Заявленная исходная длина — не подсказка, а предел:
    /// без него распаковка чужих данных превращается в способ исчерпать память одним сообщением.
    /// </remarks>
    private void ParseCompressedCertificate(ReadOnlySpan<byte> body)
    {
        if (body.Length < 2 + 3 + 3) throw new InvalidOperationException("CompressedCertificate слишком короткий");

        var algorithm = (Extensions.CertificateCompressionAlgorithm)BinaryPrimitives.ReadUInt16BigEndian(body[..2]);
        var uncompressedLength = (body[2] << 16) | (body[3] << 8) | body[4];
        var compressedLength = (body[5] << 16) | (body[6] << 8) | body[7];

        if (8 + compressedLength > body.Length) throw new InvalidOperationException("CompressedCertificate повреждён");
        if (uncompressedLength is <= 0 or > MaxCertificateMessageLength)
            throw new InvalidOperationException($"CompressedCertificate объявляет недопустимую исходную длину {uncompressedLength}");

        var compressed = body.Slice(8, compressedLength);
        var plain = new byte[uncompressedLength];

        // ★ Разбирать обязаны ЛЮБОЙ из объявленных алгоритмов. Сервер вправе выбрать любой, и
        // выбор делает он: объявив в ClientHello zlib, brotli и zstd, а умея только brotli, мы
        // обрываем рукопожатие на сервере, который предпочёл другой. Сузить объявляемый список
        // было бы проще, но он входит в отпечаток — у настоящего браузера там все три.
        var decompressed = algorithm switch
        {
            Extensions.CertificateCompressionAlgorithm.Brotli => TryDecompressBrotli(compressed, plain),
            Extensions.CertificateCompressionAlgorithm.Zlib => Inflate(compressed, plain, zlib: true),
            Extensions.CertificateCompressionAlgorithm.Zstd => Inflate(compressed, plain, zlib: false, zstd: true),
            _ => throw new NotSupportedException($"Сжатие сертификата алгоритмом {algorithm} не поддержано"),
        };

        if (decompressed != uncompressedLength)
            throw new InvalidOperationException($"Не удалось распаковать CompressedCertificate ({algorithm})");

        ParseCertificate(plain);
    }

    private static int TryDecompressBrotli(ReadOnlySpan<byte> source, Span<byte> destination)
        => System.IO.Compression.BrotliDecoder.TryDecompress(source, destination, out var written) ? written : -1;

    /// <summary>
    /// Распаковывает сертификат потоковым декодером.
    /// </summary>
    /// <param name="source">Сжатые данные.</param>
    /// <param name="destination">Буфер под исходное сообщение.</param>
    /// <param name="zlib">Обёртка zlib (RFC 1950).</param>
    /// <param name="zstd">Формат zstd.</param>
    /// <returns>Число распакованных байт либо -1.</returns>
    /// <remarks>
    /// Буфер назначения уже имеет объявленную исходную длину и служит пределом: распаковка
    /// чужих данных без предела — это способ исчерпать память одним сообщением.
    /// </remarks>
    private static int Inflate(ReadOnlySpan<byte> source, Span<byte> destination, bool zlib, bool zstd = false)
    {
        try
        {
            using var input = new System.IO.MemoryStream(source.ToArray(), writable: false);
            using System.IO.Stream decoder = zstd
                ? new IO.Compression.ZstdStream(input, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true)
                : zlib
                    ? new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true)
                    : new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true);

            var total = 0;

            while (total < destination.Length)
            {
                var read = decoder.Read(destination[total..]);
                if (read is 0) break;

                total += read;
            }

            return total;
        }
        catch (System.IO.InvalidDataException)
        {
            return -1;
        }
    }

    private void ParseCertificate(ReadOnlySpan<byte> body)
    {
        // certificate_request_context<0..255> + certificate_list<0..2^24-1>
        if (body.Length < 1) return;

        var position = 1 + body[0];
        if (position + 3 > body.Length) return;

        var listLength = (body[position] << 16) | (body[position + 1] << 8) | body[position + 2];
        position += 3;

        var end = Math.Min(position + listLength, body.Length);

        while (position + 3 <= end)
        {
            var certLength = (body[position] << 16) | (body[position + 1] << 8) | body[position + 2];
            position += 3;
            if (position + certLength > end) break;

            serverCertificates.Add(X509CertificateLoader.LoadCertificate(body.Slice(position, certLength)));
            position += certLength;

            if (position + 2 > end) break;
            var extensionsLength = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(position, 2));
            position += 2 + extensionsLength;
        }
    }

    /// <summary>
    /// Проверяет подпись сервера в сообщении CertificateVerify.
    /// </summary>
    /// <remarks>
    /// Это доказательство владения приватным ключом. Без него цепочка сертификатов ничего не
    /// гарантирует: переслать чужой валидный сертификат может кто угодно.
    /// </remarks>
    private void VerifyCertificateSignature(ReadOnlySpan<byte> body)
    {
        if (body.Length < 4) throw new InvalidOperationException("Короткое сообщение CertificateVerify");
        if (serverCertificates.Count is 0) throw new InvalidOperationException("CertificateVerify получен раньше Certificate");
        if (certificateTranscriptHash is null) throw new InvalidOperationException("Нет снимка транскрипта для проверки подписи");

        var scheme = BinaryPrimitives.ReadUInt16BigEndian(body[..2]);
        var signatureLength = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(2, 2));
        if (4 + signatureLength > body.Length) throw new InvalidOperationException("Оборванная подпись CertificateVerify");

        ServerCertificateVerifier.VerifySignature(serverCertificates[0], scheme, body.Slice(4, signatureLength), certificateTranscriptHash);
    }

    private void VerifyServerFinished(ReadOnlySpan<byte> verifyData)
    {
        if (ServerHandshakeSecret.IsEmpty) throw new InvalidOperationException("Нет серверного секрета рукопожатия");

        var finishedKey = Tls13KeySchedule.DeriveFinishedKey(HashAlgorithm, ServerHandshakeSecret.Span);
        var expected = Tls13KeySchedule.ComputeVerifyData(HashAlgorithm, finishedKey, transcript.ComputeHash(HashAlgorithm));

        // Сравнение обязано быть постоянного времени: обычное сравнение массивов здесь давало бы
        // побочный канал на проверке подлинности рукопожатия.
        if (!CryptographicOperations.FixedTimeEquals(expected, verifyData))
            throw new InvalidOperationException("Finished сервера не прошёл проверку");
    }

    private static string? ReadServerName(IEnumerable<Extensions.ITlsExtension> extensions)
    {
        foreach (var extension in extensions)
        {
            if (extension is Extensions.ServerNameTlsExtension serverName && !string.IsNullOrWhiteSpace(serverName.HostName)) return serverName.HostName;
        }

        return null;
    }

    /// <summary>
    /// Собирает все доли ключа, отправленные клиентом.
    /// </summary>
    /// <param name="extensions">Расширения ClientHello.</param>
    /// <returns>Доли ключа в порядке отправки.</returns>
    /// <remarks>
    /// Именно ВСЕ, а не первую. Браузер отправляет доли сразу для нескольких групп, и выбирает
    /// сервер — он вправе взять любую. Предполагать первую значит выводить общий секрет не тем
    /// ключом всякий раз, когда сервер предпочёл другую группу; выглядит это как «доля ключа
    /// сервера неправильной длины», то есть как ошибка чужой стороны.
    /// </remarks>
    private static IReadOnlyList<KeyShare> ReadClientKeyShares(IEnumerable<Extensions.ITlsExtension> extensions)
    {
        foreach (var extension in extensions)
        {
            if (extension is Extensions.KeyShareTlsExtension keyShare) return [.. keyShare.Entries];
        }

        return [];
    }


    /// <summary>
    /// Освобождает цепочку сертификатов сервера.
    /// </summary>
    public void Dispose()
    {
        foreach (var certificate in serverCertificates) certificate.Dispose();
        serverCertificates.Clear();

        transcript.Dispose();
    }

    /// <summary>
    /// Освобождает транскрипт: рукопожатие завершено и он больше не нужен.
    /// </summary>
    /// <remarks>
    /// Вызывается транспортом сразу после отправки завершающего полёта клиента. Все хэши,
    /// которые от транскрипта требовались, к этому мигу уже сняты, а держать его до смерти
    /// соединения — значит занимать пуловские буферы на всё время работы.
    /// </remarks>
    public void ReleaseTranscript() => transcript.Dispose();
}
