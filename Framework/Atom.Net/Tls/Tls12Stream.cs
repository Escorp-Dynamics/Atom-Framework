using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Linq;
using System.Security.Authentication;

namespace Atom.Net.Tls;

/// <summary>
/// Реализация потока TLS 1.2.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="Tls12Stream"/>.
/// </remarks>
/// <param name="settings">Настройки TLS.</param>
/// <param name="stream">Сетевой поток.</param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public class Tls12Stream([NotNull] NetworkStream stream, in TlsSettings settings) : TlsStream(stream, settings)
{
    /// <summary>
    /// Внутреннее состояние рукопожатия для двунаправленного флоу.
    /// </summary>
    private enum State
    {
        Init,
        ClientHelloSent,
        AwaitServerFlight,
        KeysComputed,
        FinishedExchanged,
        Ready,
    }

    /// <summary>
    /// Результат обработки Alert записи.
    /// </summary>
    protected enum PostAlertAction
    {
        /// <summary>
        /// Продолжить чтение следующей записи.
        /// </summary>
        Continue,
        /// <summary>
        /// Соединение корректно закрыто (close_notify).
        /// </summary>
        Eof,
        /// <summary>
        /// Фатальный alert — соединение следует разорвать.
        /// </summary>
        Fatal,
    }

    private const string ReadKeyUnset = "Ключи чтения не установлены";

    /// <summary>
    /// Единственный текст отказа для ЛЮБОЙ неподлинной записи.
    /// </summary>
    /// <remarks>
    /// ★ Раньше отказы различались по месту: «AEAD decrypt failed (TLS1.2)», «…(NST)»,
    /// «…(post-handshake)», «…(read)», «AEAD length mismatch», «Payload короче explicit IV + tag».
    /// Для AEAD это было безвредно — подлинность проверяет сам примитив. Для режима CBC
    /// различимые снаружи причины отказа И ЕСТЬ уязвимость: по ним партнёр превращается в оракул
    /// (Lucky13/POODLE, RFC 7457 §2.2). Поэтому причина не сообщается ни текстом, ни типом
    /// исключения, ни кодом алерта.
    /// </remarks>
    private const string RecordNotAuthentic = "Запись не прошла проверку подлинности";

    private readonly HandshakeTranscript transcript = new();

    private State state;
    private ReadOnlyMemory<byte> serverRandom; // 32 байта
    private ReadOnlyMemory<byte> clientRandom; // 32 байта

    /// <summary>Расширения в том порядке, в каком они ушли на провод.</summary>
    private IEnumerable<Extensions.ITlsExtension>? orderedExtensions;
    private ReadOnlyMemory<byte> premasterSecret; // зависит от KEX, для (EC)DHE получаем из ECDH
    private ushort chosenSuite;
    private NamedGroup chosenGroup; // Выбранная сервером группа (из SKE)

    // Сертификат сервера (лист) для проверки подписи SKE
    private X509Certificate2? serverLeaf;

    /// <summary>
    /// Полная цепочка сертификатов сервера (leaf первый).
    /// </summary>
    private List<X509Certificate2>? serverCerts;

    // Параметры ECDHE (сервера) и наш эфемерный ключ
    private byte[]? serverEcQx, serverEcQy;
    private ECDiffieHellman? ecdhe;

    // Секреты и ключи
    private byte[]? masterSecret;         // 48 байт
    private byte[]? clientWriteKey;       // 16/32
    private byte[]? serverWriteKey;       // 16/32
    private byte[]? clientWriteIvSalt;    // 4
    private byte[]? serverWriteIvSalt;    // 4

    /// <summary>
    /// MAC-ключи для наборов на CBC.
    /// </summary>
    /// <remarks>
    /// У AEAD их нет вовсе: подлинность там обеспечивает сам примитив. У CBC подлинность —
    /// отдельный HMAC на отдельном ключе, и в key_block эти ключи идут ПЕРВЫМИ (RFC 5246 §6.3).
    /// </remarks>
    private byte[]? clientWriteMacKey, serverWriteMacKey;

    // AEAD и счётчики для 1.2
    private IAeadCipher? aeadWrite12, aeadRead12;

    /// <summary>Защита записи в режиме CBC — только для наборов «…_CBC_SHA».</summary>
    private Tls12CbcRecordProtection? cbcWrite12, cbcRead12;

    private ulong seqWrite12, seqRead12;

    private byte[]? decCache;
    private int decPos, decRemaining;
    private byte[]? handshakeFragmentBuffer;
    private int handshakeFragmentLength;
    [SuppressMessage("Major Code Smell", "S4487:Unread private fields should be removed", Justification = "Temporary TLS handshake diagnostics for dump inspection.")]
    private TlsHandshakeType lastHandshakeType;
    [SuppressMessage("Major Code Smell", "S4487:Unread private fields should be removed", Justification = "Temporary TLS handshake diagnostics for dump inspection.")]
    private int lastHandshakeLength;
    [SuppressMessage("Major Code Smell", "S4487:Unread private fields should be removed", Justification = "Temporary TLS handshake diagnostics for dump inspection.")]
    private int lastHandshakeAvailable;
    [SuppressMessage("Major Code Smell", "S4487:Unread private fields should be removed", Justification = "Temporary TLS record diagnostics for dump inspection.")]
    private byte lastRecordType;
    [SuppressMessage("Major Code Smell", "S4487:Unread private fields should be removed", Justification = "Temporary TLS record diagnostics for dump inspection.")]
    private ushort lastRecordVersion;
    [SuppressMessage("Major Code Smell", "S4487:Unread private fields should be removed", Justification = "Temporary TLS record diagnostics for dump inspection.")]
    private int lastRecordLength;
    [SuppressMessage("Major Code Smell", "S4487:Unread private fields should be removed", Justification = "Temporary TLS record diagnostics for dump inspection.")]
    private byte lastPayloadByte0;
    [SuppressMessage("Major Code Smell", "S4487:Unread private fields should be removed", Justification = "Temporary TLS record diagnostics for dump inspection.")]
    private byte lastPayloadByte1;
    [SuppressMessage("Major Code Smell", "S4487:Unread private fields should be removed", Justification = "Temporary TLS record diagnostics for dump inspection.")]
    private byte lastPayloadByte2;
    [SuppressMessage("Major Code Smell", "S4487:Unread private fields should be removed", Justification = "Temporary TLS record diagnostics for dump inspection.")]
    private byte lastPayloadByte3;

    private bool useEms;

    // Имя хоста из SNI (server_name), извлекаем при построении ClientHello
    private string? sniHost;

    private byte[]? serverX25519Pub;

    /// <summary>
    /// Согласован ли набор на основе ChaCha20-Poly1305.
    /// </summary>
    /// <remarks>
    /// Определяет форму записи: у ChaCha20 в TLS 1.2 НЕТ явного вектора инициализации, а nonce
    /// получается сложением по модулю два фиксированного IV со счётчиком — ровно как в TLS 1.3.
    /// У AES-GCM запись начинается с восьми байт явного IV. Перепутать эти две формы значит
    /// получить провал расшифровки на первой же записи.
    /// </remarks>
    private bool IsChaChaSuite
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get { EnsureSuiteShape(); return shapeBulk is Tls12BulkCipher.ChaCha20Poly1305; }
    }

    /// <summary>
    /// Согласован ли набор на основе AES-CBC с отдельным HMAC.
    /// </summary>
    /// <remarks>
    /// Форма записи у CBC третья, не совпадающая ни с AES-GCM, ни с ChaCha20: явный вектор
    /// шестнадцатибайтовый и СЛУЧАЙНЫЙ, а длина открытых данных заранее неизвестна — она
    /// выясняется только после снятия дополнения.
    /// </remarks>
    private bool IsCbcSuite
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get { EnsureSuiteShape(); return shapeBulk is Tls12BulkCipher.AesCbc; }
    }

    /// <summary>Длина явного вектора инициализации в записи для согласованного набора.</summary>
    /// <remarks>
    /// Значение берётся из общей таблицы, а не из перечисления кодов по месту: перечисления
    /// расходятся между собой молча, и расхождение здесь означало бы провал расшифровки на первой
    /// же записи — то есть «сервер сломался» вместо «мы забыли строчку».
    /// </remarks>
    private int RecordExplicitIvLength
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get { EnsureSuiteShape(); return shapeRecordIvLength; }
    }

    // --- Снимок свойств согласованного набора ------------------------------------------------
    // ★ Зачем снимок, а не чтение таблицы по месту. Свойства набора нужны по нескольку раз НА
    // КАЖДУЮ запись, а таблица возвращает структуру из восьми полей: замер показал 4,0 нс на
    // обращение против 1,6 нс у прежнего перечисления кодов. На запись в 16 КиБ это неразличимо,
    // но на потоке коротких кадров HTTP/2 набегает уже заметно, а производительность здесь —
    // главное требование. Снимок оставляет таблицу единственным источником правды и сводит
    // горячий путь к сравнению двух байт.
    //
    // Ключом служит сам chosenSuite, а не флаг «инициализировано»: набор известен только после
    // ServerHello, и подмена поля (в том числе тестами) обязана подхватываться, а не игнорироваться.
    // Автосвойство здесь невозможно: значение не хранится само по себе, а обновляется вслед за
    // chosenSuite, и подсказка анализатора «сделайте auto property» стёрла бы именно эту связь.
#pragma warning disable IDE0032 // Use auto property
    private ushort shapeSuite = 0xFFFF;
    private Tls12BulkCipher shapeBulk;
    private int shapeRecordIvLength = 8;
    private Tls12KeyExchange shapeKeyExchange;
    private HashAlgorithmName shapePrfHash = HashAlgorithmName.SHA256;
#pragma warning restore IDE0032 // Use auto property

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureSuiteShape()
    {
        if (shapeSuite == chosenSuite) return;

        RefreshSuiteShape();
    }

    /// <summary>
    /// Пересчитывает снимок свойств набора.
    /// </summary>
    /// <remarks>
    /// Вынесено отдельно и без встраивания: срабатывает один раз за соединение, а раздувать им
    /// тело каждого обращения к свойству — значит потерять смысл снимка.
    ///
    /// Значения по умолчанию для НЕизвестного набора совпадают с прежним поведением (AES-GCM,
    /// восьмибайтовый явный вектор, PRF на SHA-256). До ServerHello набор равен нулю, и любое
    /// другое умолчание меняло бы форму записи задним числом.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RefreshSuiteShape()
    {
        if (Tls12CipherSuiteParameters.TryGet(chosenSuite, out var parameters))
        {
            shapeBulk = parameters.Bulk;
            shapeRecordIvLength = parameters.RecordIvLength;
            shapeKeyExchange = parameters.KeyExchange;
            shapePrfHash = parameters.PrfHash;
        }
        else
        {
            shapeBulk = Tls12BulkCipher.AesGcm;
            shapeRecordIvLength = 8;
            shapeKeyExchange = Tls12KeyExchange.Ecdhe;
            shapePrfHash = HashAlgorithmName.SHA256;
        }

        shapeSuite = chosenSuite;
    }

    /// <summary>Собственный эфемерный закрытый ключ X25519 для этого рукопожатия.</summary>
    private byte[]? x25519Private;

    /// <summary>Открытая часть эфемерного ключа X25519.</summary>
    private byte[]? x25519Public;

    /// <summary>
    /// Согласованный ALPN ("h2" или "http/1.1").
    /// </summary>
    public ReadOnlyMemory<byte> NegotiatedAlpn { get; protected set; }

    /// <summary>
    /// Предлагали ли мы в этом рукопожатии TLS 1.3.
    /// </summary>
    /// <remarks>
    /// Определяет, применима ли проверка сигнала понижения версии: она осмысленна лишь тогда,
    /// когда более высокая версия действительно предлагалась.
    /// </remarks>
    protected bool OffersTls13
        => Settings.MaxVersion.HasFlag(SslProtocols.Tls13)
        || Settings.Extensions.OfType<Extensions.SupportedVersionsTlsExtension>().Any(static extension => extension.Versions.Contains(SslProtocols.Tls13));

    /// <summary>
    /// Сервер запросил клиентский сертификат (CertificateRequest).
    /// </summary>
    private bool serverRequestedClientCert;

    /// <summary>
    /// Приходил ли ServerKeyExchange.
    /// </summary>
    /// <remarks>
    /// Нужен для перекрёстной проверки «набор ↔ наличие сообщения» (RFC 5246 §7.4.3): у эфемерного
    /// обмена оно обязательно, у обмена на RSA его быть не должно. Оба нарушения без этой проверки
    /// проходили молча и вылезали лишь на несовпадении Finished.
    /// </remarks>
    private bool serverKeyExchangeReceived;

    /// <summary>
    /// Способ обмена ключами согласованного набора.
    /// </summary>
    private Tls12KeyExchange KeyExchangeKind
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get { EnsureSuiteShape(); return shapeKeyExchange; }
    }

    /// <summary>
    /// Выбор хэш-алгоритма PRF по выбранному шифросьюту (SHA256/384).
    /// </summary>
    /// <remarks>
    /// ★ Перечислять здесь коды «на глаз» нельзя. Набор 0x009D (RSA + AES-256-GCM) тоже требует
    /// SHA-384, и прежнее условие «0xC030 или 0xC02C» его не знало: master secret вышел бы
    /// неверным, а провалилось бы это в самом конце внешне безупречного рукопожатия — на проверке
    /// Finished, то есть выглядело бы как ошибка сервера.
    /// </remarks>
    private HashAlgorithmName PrfHash
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get { EnsureSuiteShape(); return shapePrfHash; }
    }

    /// <summary>
    /// Сырые bytes OCSP stapling из CertificateStatus (может быть пусто).
    /// </summary>
    public ReadOnlyMemory<byte> StapledOcspResponse { get; protected set; }

    /// <summary>
    /// Сырой тикет TLS 1.2 (RFC 5077), если прислан сервером (может быть пусто).
    /// </summary>
    public ReadOnlyMemory<byte> SessionTicket { get; protected set; }

    /// <summary>
    /// Подсказка по времени жизни тикета (секунды) из NST (TLS 1.2).
    /// </summary>
    public uint SessionTicketLifetimeHint { get; protected set; }

    /// <summary>
    /// Шифрует и отправляет одну TLS 1.2 запись (AEAD). Никаких лишних аллокаций:
    /// explicitIV + ciphertext + tag пишутся в арендованный буфер.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask SendTls12RecordAsync(TlsContentType type, ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken)
    {
        var plainLen = plaintext.Length;
        var payloadLen = ProtectedRecordLength(plainLen);
        var payload = ArrayPool<byte>.Shared.Rent(payloadLen);

        try
        {
            ProtectRecord(type, plaintext.Span, payload.AsSpan(0, payloadLen));

            await SendPlainRecordAsync(type, payload.AsMemory(0, payloadLen), cancellationToken).ConfigureAwait(false);
            seqWrite12++;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload.AsSpan(0, payloadLen));
            ArrayPool<byte>.Shared.Return(payload, clearArray: false);
        }
    }

    /// <summary>
    /// Возвращает длину тела записи, в которое превратится указанное количество открытых данных.
    /// </summary>
    /// <param name="plainLength">Длина открытых данных.</param>
    /// <returns>Длина тела записи без пятибайтового заголовка.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ProtectedRecordLength(int plainLength)
    {
        if (cbcWrite12 is not null) return Tls12CbcRecordProtection.ProtectedLength(plainLength);
        if (aeadWrite12 is null || clientWriteIvSalt is null) throw new InvalidOperationException("Ключи записи не установлены");

        return RecordExplicitIvLength + plainLength + aeadWrite12.TagSize;
    }

    /// <summary>
    /// Единственное место, где запись TLS 1.2 защищается.
    /// </summary>
    /// <param name="type">Тип содержимого.</param>
    /// <param name="plain">Открытые данные.</param>
    /// <param name="record">Буфер тела записи ровно на <see cref="ProtectedRecordLength(int)"/> байт.</param>
    /// <remarks>
    /// Счётчик <c>seqWrite12</c> здесь НЕ увеличивается: он принадлежит отправке, а отправка может
    /// не состояться. Увеличивает его вызывающий, ровно один раз, после успешной записи в сокет.
    /// </remarks>
    private void ProtectRecord(TlsContentType type, ReadOnlySpan<byte> plain, Span<byte> record)
    {
        Span<byte> nonce = stackalloc byte[12];
        Span<byte> header = stackalloc byte[13];

        if (cbcWrite12 is not null)
        {
            BuildAadAndNonceForWrite(type, plain.Length, seqWrite12, default, nonce, header);
            _ = cbcWrite12.Protect(header, plain, record);
            return;
        }

        if (aeadWrite12 is null) throw new InvalidOperationException("Ключи записи не установлены");

        var explicitIvLength = RecordExplicitIvLength;
        var explicitIv = record[..explicitIvLength];
        var cipherTag = record[explicitIvLength..];

        BuildAadAndNonceForWrite(type, plain.Length, seqWrite12, explicitIv, nonce, header);

        if (!aeadWrite12.TryEncrypt(nonce, header, plain, cipherTag, out var written) || written != cipherTag.Length)
            throw new CryptographicException("AEAD encrypt failed (TLS1.2)");
    }

    /// <summary>
    /// Верхняя оценка длины открытых данных для записи указанного размера.
    /// </summary>
    /// <param name="recordLength">Длина тела записи.</param>
    /// <returns>Сколько байт достаточно арендовать под расшифровку.</returns>
    /// <remarks>
    /// Для AEAD длина известна точно, для CBC — нет: она выясняется лишь после снятия дополнения,
    /// а до тех пор всё, что о ней известно, — что она не больше шифротекста. Отсюда оценка
    /// сверху и обязательный <c>out plainLength</c> у снятия защиты.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int MaxPlaintextForRecord(int recordLength)
    {
        if (cbcRead12 is not null) return Math.Max(0, recordLength - Tls12CbcRecordProtection.BlockLength);

        var overhead = RecordExplicitIvLength + (aeadRead12?.TagSize ?? 16);
        return Math.Max(0, recordLength - overhead);
    }

    /// <summary>
    /// Единственное место, где с записи TLS 1.2 снимается защита.
    /// </summary>
    /// <param name="type">Тип содержимого записи.</param>
    /// <param name="record">Тело записи как оно пришло с провода.</param>
    /// <param name="plain">Буфер не меньше <see cref="MaxPlaintextForRecord(int)"/>.</param>
    /// <param name="plainLength">Длина открытых данных.</param>
    /// <returns><see langword="true"/>, если запись подлинна.</returns>
    /// <remarks>
    /// ★ Раньше эти десять строк были скопированы в ПЯТЬ мест (Finished, тикеты, post-handshake,
    /// алерты, прикладные данные), и счётчик чтения увеличивался в каждом отдельно. Для AEAD такие
    /// копии просто расходились бы со временем; для CBC они означали бы пять разных проверок
    /// подлинности и пять разных путей отказа — то есть ровно ту различимость, на которой стоит
    /// Lucky13. Причина отказа не сообщается: вызывающая сторона обязана превратить
    /// <see langword="false"/> в один и тот же отказ.
    /// </remarks>
    private bool TryUnprotectRecord(TlsContentType type, ReadOnlySpan<byte> record, Span<byte> plain, out int plainLength)
    {
        plainLength = 0;

        Span<byte> nonce = stackalloc byte[12];
        Span<byte> header = stackalloc byte[13];

        if (cbcRead12 is not null)
        {
            // Длину в заголовке MAC дописывает сам разбор CBC: до снятия дополнения она неизвестна.
            BuildAadAndNonceForRead(type, plainLen: 0, seqRead12, default, nonce, header);

            if (!cbcRead12.TryUnprotect(record, header, plain, out plainLength)) return default;

            seqRead12++;
            return true;
        }

        if (aeadRead12 is null || serverWriteIvSalt is null) throw new InvalidOperationException(ReadKeyUnset);

        var explicitIvLength = RecordExplicitIvLength;
        if (record.Length < explicitIvLength + aeadRead12.TagSize) return default;

        var explicitIv = record[..explicitIvLength];
        var cipherTag = record[explicitIvLength..];
        var expected = cipherTag.Length - aeadRead12.TagSize;

        if (plain.Length < expected) return default;

        BuildAadAndNonceForRead(type, expected, seqRead12, explicitIv, nonce, header);

        if (!aeadRead12.TryDecrypt(nonce, header, cipherTag, plain[..expected], out var got) || got != expected) return default;

        plainLength = expected;
        seqRead12++;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask<OwnerMemory> ReceiveTls12RecordAsync(TlsContentType expectType, CancellationToken cancellationToken)
    {
        var (hdr, payload) = await ReadRecordAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (hdr.ContentType != expectType) throw new InvalidOperationException($"Ожидался {expectType}");

            var plain = ArrayPool<byte>.Shared.Rent(MaxPlaintextForRecord(hdr.Length));

            if (!TryUnprotectRecord(expectType, payload.AsSpan(0, hdr.Length), plain, out var plainLen))
            {
                ArrayPool<byte>.Shared.Return(plain, clearArray: true);
                throw new CryptographicException(RecordNotAuthentic);
            }

            return new OwnerMemory(plain, plainLen);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload.AsSpan());
            ArrayPool<byte>.Shared.Return(payload, clearArray: true);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ParseServerHello(ReadOnlySpan<byte> body)
    {
        // version(2)=0x0303 | random(32) | sid_len(1)+sid | suite(2) | comp(1) | [exts_len(2)+exts]
        if (body.Length < 2 + 32 + 1 + 2 + 1) throw new InvalidOperationException("ServerHello слишком короткий");
        if (body[0] is not 0x03 || body[1] is not 0x03) throw new InvalidOperationException("Не TLS 1.2");

        var sr = new byte[32];
        body.Slice(2, 32).CopyTo(sr);
        serverRandom = sr;

        // Сигнал понижения версии проверяется ТОЛЬКО если мы сами предлагали TLS 1.3.
        // По RFC 8446 §4.1.3 сервер, поддерживающий 1.3, обязан ставить эту метку в ответ
        // клиенту, который 1.3 не предложил, — и для такого клиента она означает не атаку, а
        // штатный ответ. Проверять её безусловно значит объявить враждебным любой современный
        // сервер: именно так профили TLS 1.2 и переставали работать с Cloudflare.
        if (OffersTls13 && IsDowngrade(serverRandom.Span))
            throw new InvalidOperationException("Возможный downgrade detected (server_random sentinel)");

        var pos = 2 + 32;
        var sidLen = body[pos++]; pos += sidLen;

        chosenSuite = (ushort)((body[pos] << 8) | body[pos + 1]);
        pos += 2;

        if (body[pos++] is not 0) throw new NotSupportedException("Compression != null");

        // exts (опционально)
        if (pos + 2 <= body.Length)
        {
            var extLen = (body[pos] << 8) | body[pos + 1];
            pos += 2;
            var exts = body.Slice(pos, extLen);

            if (!TryReadServerHelloExtensions(exts, out var ems, out var encryptThenMac, out var alpn))
                throw new InvalidOperationException("ServerHello extensions corrupted");

            // ★ encrypt_then_mac (RFC 7366) мы не предлагаем ни в одном профиле браузера, а значит
            // сервер не вправе его согласовать. Раньше любое неизвестное расширение молча
            // игнорировалось — и согласуй сервер этот режим, мы разбирали бы записи в ОБРАТНОМ
            // порядке (сначала MAC, потом расшифровка). Симптом — сплошной провал MAC на всех
            // записях, причина — невидима.
            if (encryptThenMac)
                throw new InvalidOperationException("Сервер согласовал encrypt_then_mac, которого мы не предлагали");

            useEms = ems;

            if (!alpn.IsEmpty)
            {
                NegotiatedAlpn = alpn;

                // Дублируем результат в общее строковое свойство: выбор протокола нужен слою
                // соединений одинаково для обеих версий TLS, и заставлять его разбирать байты
                // означало бы протащить формат расширения туда, где ему не место.
                NegotiatedProtocol = Encoding.ASCII.GetString(alpn.Span);
            }
        }

        // ★ Две проверки, а не одна, и в этом порядке. Раньше здесь стояли ДВА перечисления кодов,
        // записанные по-разному, и правка одного без другого давала тот же обрыв с другим текстом.
        // Теперь обе опираются на общую таблицу, но разделены по смыслу: «не предлагали» — это
        // нарушение со стороны сервера (или наша дыра в мимикрии), «не реализовано» — наша
        // недоделка. Смешивать их в одном сообщении значило бы каждый раз гадать, чей это дефект.
        if (!IsOfferedSuite(chosenSuite, Settings.CipherSuites))
            throw new InvalidOperationException($"Сервер выбрал не предлагавшийся suite 0x{chosenSuite:X4}");

        if (!IsImplementedSuite(chosenSuite))
            throw new NotSupportedException($"Набор 0x{chosenSuite:X4} предлагается ради отпечатка ja3/ja4, но в этом клиенте не реализован");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ParseCertificate(ReadOnlySpan<byte> body)
    {
        // struct {
        //   opaque certificate_list<0..2^24-1>;
        // } Certificate;
        if (body.Length < 3) throw new InvalidOperationException("Certificate пуст");

        var listLen = (body[0] << 16) | (body[1] << 8) | body[2];
        var pos = 3;

        if (pos + listLen > body.Length) throw new InvalidOperationException("Certificate list повреждён");
        if (listLen < 3) throw new InvalidOperationException("Нет сертификатов");

        // Очистим предыдущую цепочку
        if (serverCerts is not null)
        {
            foreach (var c in serverCerts) c.Dispose();
            serverCerts.Clear();
        }
        else
        {
            serverCerts = new(4);
        }

        // Разбор последовательности cert_len(3)+cert
        var end = pos + listLen;

        while (pos + 3 <= end)
        {
            var cLen = (body[pos] << 16) | (body[pos + 1] << 8) | body[pos + 2];
            pos += 3;

            if (pos + cLen > end) throw new InvalidOperationException("Повреждённый certificate entry");

            var certSpan = body.Slice(pos, cLen);
            pos += cLen;

            var cert = X509CertificateLoader.LoadCertificate(certSpan);
            serverCerts.Add(cert);
        }

        if (serverCerts.Count == 0) throw new InvalidOperationException("Пустая цепочка сертификатов сервера");

        serverLeaf?.Dispose();
        serverLeaf = serverCerts[0];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ParseServerKeyExchange(ReadOnlySpan<byte> body)
    {
        // curve_type(1)=3 | named_curve(2) | pub_len(1)+pub_key | hash(1) | sigalg(1) | sig_len(2) | sig
        var pos = 0;
        if (body[pos++] is not 3) throw new NotSupportedException("Ожидался named_curve");

        var named = (ushort)((body[pos] << 8) | body[pos + 1]);
        pos += 2;

        // 1) Запоминаем выбранную сервером группу и 2) разбираем её публичный ключ.
        // Оба правила живут в отдельных методах и вызываются отсюда. Раньше эти же две таблицы
        // стояли здесь дословно, а MapServerNamedGroup/UnpackServerPublicKey висели рядом
        // невостребованными — две копии одного правила расходятся всегда, и расхождение в разборе
        // ключа сервера означало бы либо отказ на исправном сервере, либо принятую чужую точку.
        chosenGroup = MapServerNamedGroup(named);

        var pkLen = body[pos++];
        if (pkLen <= 0) throw new NotSupportedException("Пустой публичный ключ сервера");

        var pub = body.Slice(pos, pkLen);
        pos += pkLen;

        UnpackServerPublicKey(chosenGroup, pub);

        if (pos + 4 > body.Length) throw new InvalidOperationException("Нет подписи SKE");

        var hashId = body[pos++];
        var sigAlg = body[pos++];
        var sigLen = (body[pos] << 8) | body[pos + 1];
        pos += 2;

        var sig = body.Slice(pos, sigLen);

        // 'pos' сейчас стоит на начале подписи (после 1+1+2).
        // Подписанные параметры = body без (hash,sigAlg,sig_len,signature).
        var signedParams = body[..(pos - 4)];
        VerifySkeSignature(hashId, sigAlg, sig, signedParams);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureEcdheForGroup()
    {
        // X25519 обслуживается собственной реализацией: платформа отказывает на кривой
        // 1.3.101.110 (PlatformNotSupportedException), а это ОСНОВНАЯ группа современных
        // браузеров — без неё профиль пришлось бы уводить на кривые NIST и терять сходство.
        if (chosenGroup is NamedGroup.X25519)
        {
            if (x25519Private is not null) return;

            x25519Private = X25519.CreatePrivateKey();
            x25519Public = X25519.GetPublicKey(x25519Private);
            return;
        }

        if (ecdhe is not null) return;

        ecdhe = chosenGroup switch
        {
            NamedGroup.Secp256r1 => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256),
            NamedGroup.Secp384r1 => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384),
            NamedGroup.Secp521r1 => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP521),
            _ => throw new NotSupportedException("Группа не поддержана")
        };
    }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "TLS 1.2 ServerKeyExchange verification may require legacy SHA-1 for interoperability with real servers and local SslStream peers.")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void VerifySkeSignature(byte hashId, byte sigAlg, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> skeParams)
    {
        if (serverLeaf is null) throw new InvalidOperationException("Нет server certificate для проверки SKE");

        // signed_data = client_random || server_random || skeParams
        var toSign = ArrayPool<byte>.Shared.Rent(64 + skeParams.Length);

        try
        {
            clientRandom.Span.CopyTo(toSign.AsSpan(0, 32));
            serverRandom.Span.CopyTo(toSign.AsSpan(32, 32));
            skeParams.CopyTo(toSign.AsSpan(64));

            var hashAlg = hashId switch
            {
                2 => HashAlgorithmName.SHA1,
                4 => HashAlgorithmName.SHA256,
                5 => HashAlgorithmName.SHA384,
                6 => HashAlgorithmName.SHA512,
                8 when sigAlg is 4 => HashAlgorithmName.SHA256,
                8 when sigAlg is 5 => HashAlgorithmName.SHA384,
                8 when sigAlg is 6 => HashAlgorithmName.SHA512,
                _ => throw new NotSupportedException($"HashAlgorithm в SKE не поддержан: 0x{hashId:X2}, sig=0x{sigAlg:X2}"),
            };

            var rsaPadding = hashId is 8 && sigAlg is 4 or 5 or 6
                ? RSASignaturePadding.Pss
                : RSASignaturePadding.Pkcs1;

            Span<byte> digest = stackalloc byte[
                hashAlg == HashAlgorithmName.SHA1 ? 20 :
                hashAlg == HashAlgorithmName.SHA384 ? 48 :
                hashAlg == HashAlgorithmName.SHA512 ? 64 : 32];

            if (hashAlg == HashAlgorithmName.SHA1)
#pragma warning disable S4790 // Weak hashing algorithms should not be used
                SHA1.HashData(toSign.AsSpan(0, 64 + skeParams.Length), digest);
#pragma warning restore S4790 // Weak hashing algorithms should not be used
            else if (hashAlg == HashAlgorithmName.SHA256)
                SHA256.HashData(toSign.AsSpan(0, 64 + skeParams.Length), digest);
            else if (hashAlg == HashAlgorithmName.SHA384)
                SHA384.HashData(toSign.AsSpan(0, 64 + skeParams.Length), digest);
            else
                SHA512.HashData(toSign.AsSpan(0, 64 + skeParams.Length), digest);

            using var rsa = serverLeaf.GetRSAPublicKey();

            if (rsa is not null && (sigAlg is 1 || (hashId is 8 && sigAlg is 4 or 5 or 6)))
            {
                if (!rsa.VerifyHash(digest, signature, hashAlg, rsaPadding))
                    throw new CryptographicException("RSA-подпись SKE неверна");

                return;
            }

            using var ecdsa = serverLeaf.GetECDsaPublicKey();

            if (ecdsa is not null && sigAlg is 3)
            {
                // Подпись в TLS передаётся ПОСЛЕДОВАТЕЛЬНОСТЬЮ DER, а не парой r‖s фиксированной
                // длины, которую VerifyHash подразумевает по умолчанию. Без явного указания
                // формата проверка не проходит НИКОГДА — а выглядит это как «сервер прислал
                // неверную подпись», то есть как чужая ошибка.
                if (!ecdsa.VerifyHash(digest, signature, DSASignatureFormat.Rfc3279DerSequence))
                    throw new CryptographicException("ECDSA-подпись SKE неверна");
                return;
            }

            throw new NotSupportedException("Тип ключа/подписи сервера не поддержан");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(toSign, clearArray: true);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ComputeVerifyData(Span<byte> dst, bool forServer)
    {
        var prfHash = PrfHash;
        var snap = transcript.ComputeHash(prfHash); // снимок без модификации
        var label = forServer ? "server finished"u8 : "client finished"u8;
        Tls12Prf(masterSecret, label, snap, dst, prfHash);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DeriveSecretsTls12AfterClientMessagesAppended()
    {
        // 1) premaster уже получен (см. ниже, ComputePremasterFromServerParams)
        // 2) EMS/legacy — считаем по СНИМКУ транскрипта, где уже есть клиентский Certificate (если был) и CKE.
        var prfHash = PrfHash;

        if (useEms)
        {
            var sessionHash = transcript.ComputeHash(prfHash);
            masterSecret = new byte[48];
            Tls12Prf(premasterSecret.Span, "extended master secret"u8, sessionHash, masterSecret, prfHash);
        }
        else
        {
            Span<byte> seed = stackalloc byte[64];
            clientRandom.Span.CopyTo(seed[..32]);
            serverRandom.Span.CopyTo(seed[32..]);
            masterSecret = new byte[48];
            Tls12Prf(premasterSecret.Span, "master secret"u8, seed, masterSecret, prfHash);
        }

        // key_block. Длины берутся из общей таблицы: ChaCha20 — 32-байтовый ключ и 12-байтовый
        // фиксированный IV, AES-GCM — 16/32 байта ключа и 4 байта соли, CBC — 16/32 байта ключа,
        // 20-байтовый MAC-ключ и НОЛЬ байт фиксированного вектора (он явный и случайный).
        if (!Tls12CipherSuiteParameters.TryGet(chosenSuite, out var suite))
            throw new NotSupportedException($"Неподдерживаемый suite 0x{chosenSuite:X4}");

        var keyLen = suite.EncKeyLength;
        var ivLen = suite.FixedIvLength;
        var macLen = suite.MacKeyLength;
        var blockLen = (macLen * 2) + (keyLen * 2) + (ivLen * 2);
        var keyBlock = new byte[blockLen];

        Span<byte> seedKb = stackalloc byte[64];
        serverRandom.Span.CopyTo(seedKb[..32]);
        clientRandom.Span.CopyTo(seedKb[32..]);
        Tls12Prf(masterSecret, "key expansion"u8, seedKb, keyBlock, prfHash);

        var p = 0;

        // ★ Порядок задан RFC 5246 §6.3 и начинается ИМЕННО с MAC-ключей. Поставить их после
        // ключей шифрования — значит получить корректные по длине, но неверные ключи: рукопожатие
        // дойдёт до конца и рассыплется на проверке Finished, будто виноват сервер.
        if (macLen > 0)
        {
            clientWriteMacKey = keyBlock.AsSpan(p, macLen).ToArray();
            p += macLen;

            serverWriteMacKey = keyBlock.AsSpan(p, macLen).ToArray();
            p += macLen;
        }

        clientWriteKey = keyBlock.AsSpan(p, keyLen).ToArray();
        p += keyLen;

        serverWriteKey = keyBlock.AsSpan(p, keyLen).ToArray();
        p += keyLen;

        clientWriteIvSalt = keyBlock.AsSpan(p, ivLen).ToArray();
        p += ivLen;

        serverWriteIvSalt = keyBlock.AsSpan(p, ivLen).ToArray();

        aeadWrite12?.Dispose();
        aeadRead12?.Dispose();
        cbcWrite12?.Dispose();
        cbcRead12?.Dispose();
        aeadWrite12 = aeadRead12 = null;
        cbcWrite12 = cbcRead12 = null;

        switch (suite.Bulk)
        {
            case Tls12BulkCipher.ChaCha20Poly1305:
                aeadWrite12 = new ChaCha20Poly1305Aead(clientWriteKey);
                aeadRead12 = new ChaCha20Poly1305Aead(serverWriteKey);
                break;

            case Tls12BulkCipher.AesCbc:
                cbcWrite12 = new Tls12CbcRecordProtection(clientWriteKey, clientWriteMacKey!);
                cbcRead12 = new Tls12CbcRecordProtection(serverWriteKey, serverWriteMacKey!);
                break;

            default:
                aeadWrite12 = new AesGcmAead(clientWriteKey);
                aeadRead12 = new AesGcmAead(serverWriteKey);
                break;
        }

        seqWrite12 = seqRead12 = 0;

        CryptographicOperations.ZeroMemory(keyBlock);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlyMemory<byte> BuildClientKeyExchangeBodyAndAppend()
    {
        if (KeyExchangeKind is Tls12KeyExchange.Rsa) return BuildRsaClientKeyExchangeAndAppend();

        EnsureEcdheForGroup();

        ReadOnlyMemory<byte> cke;

        if (chosenGroup is NamedGroup.X25519)
        {
            if (x25519Public is not { Length: 32 }) throw new CryptographicException("X25519 pub invalid");
            if (serverX25519Pub is not { Length: 32 }) throw new CryptographicException("X25519: сервер не прислал 32-байтовый ключ");

            Span<byte> body = stackalloc byte[1 + 32];
            body[0] = 32;
            x25519Public.CopyTo(body[1..]);

            cke = BuildHandshake(TlsHandshakeType.ClientKeyExchange, body);
            transcript.Append(cke.Span);

            premasterSecret = X25519.DeriveSharedSecret(x25519Private!, serverX25519Pub);
            return cke;
        }
        else
        {
            // P-256/384/521 как было (ANSI X9.62 uncompressed)
            var fieldLen = chosenGroup switch
            {
                NamedGroup.Secp256r1 => 32,
                NamedGroup.Secp384r1 => 48,
                NamedGroup.Secp521r1 => 66,
                _ => throw new NotSupportedException()
            };

            var pub = ecdhe!.ExportParameters(includePrivateParameters: false);
            if (pub.Q.X is null || pub.Q.Y is null) throw new CryptographicException("ECDHE public invalid");

            Span<byte> q = stackalloc byte[1 + (fieldLen * 2)];
            q[0] = 0x04;
            pub.Q.X.CopyTo(q.Slice(1, fieldLen));
            pub.Q.Y.CopyTo(q.Slice(1 + fieldLen, fieldLen));

            Span<byte> body = stackalloc byte[1 + q.Length];
            body[0] = (byte)q.Length; q.CopyTo(body[1..]);

            cke = BuildHandshake(TlsHandshakeType.ClientKeyExchange, body);
            transcript.Append(cke.Span);

            var curve = chosenGroup switch
            {
                NamedGroup.Secp256r1 => ECCurve.NamedCurves.nistP256,
                NamedGroup.Secp384r1 => ECCurve.NamedCurves.nistP384,
                NamedGroup.Secp521r1 => ECCurve.NamedCurves.nistP521,
                _ => throw new NotSupportedException()
            };

            using var srv = ECDiffieHellman.Create(curve);

            srv.ImportParameters(new ECParameters
            {
                Curve = curve,
                Q = new ECPoint { X = serverEcQx, Y = serverEcQy },
            });

            premasterSecret = ecdhe.DeriveRawSecretAgreement(srv.PublicKey);
            return cke;
        }
    }

    /// <summary>
    /// ClientKeyExchange для наборов с обменом ключами на RSA (0x009C, 0x009D, 0x002F, 0x0035).
    /// </summary>
    /// <returns>Готовое сообщение, уже добавленное в транскрипт.</returns>
    /// <remarks>
    /// Здесь нет ServerKeyExchange вовсе: premaster придумывает клиент и зашифровывает его
    /// ОТКРЫТЫМ ключом сервера из сертификата (RSAES-PKCS1-v1_5, RFC 5246 §7.4.7.1).
    ///
    /// ★ Отдельной проверки подлинности сервера в этой ветке нет — и это не пропуск. Сервер
    /// доказывает владение закрытым ключом тем, что сумел расшифровать premaster и прислать
    /// верный Finished; ошибись он ключом — verify_data не сойдётся. Не написав этого, следующий
    /// читатель будет искать здесь «потерянную» проверку подписи и, не найдя, добавит лишнюю.
    /// </remarks>
    [SuppressMessage("Security", "CA5373:Do not use obsolete key derivation function", Justification = "Схема обмена ключами задана набором шифров TLS 1.2; выбор делает сервер.")]
    private ReadOnlyMemory<byte> BuildRsaClientKeyExchangeAndAppend()
    {
        if (serverLeaf is null) throw new InvalidOperationException("Нет сертификата сервера для обмена ключами RSA");

        using var rsa = serverLeaf.GetRSAPublicKey()
            ?? throw new CryptographicException("Набор с обменом ключами RSA выбран под сертификат без ключа RSA");

        var premaster = new byte[48];

        try
        {
            // ★ Первые два байта — версия из НАШЕГО ClientHello, а не согласованная. Сервер сверяет
            // её, чтобы поймать откат версии (RFC 5246 §7.4.7.1). Наш ClientHelloBuilder всегда
            // пишет в legacy_version 0x0303 — если он когда-нибудь начнёт писать другое, эту
            // константу придётся менять здесь же, иначе часть серверов начнёт молча отвергать
            // рукопожатие «по неизвестной причине».
            premaster[0] = 0x03;
            premaster[1] = 0x03;
            RandomNumberGenerator.Fill(premaster.AsSpan(2));

            // ★ Дополнение — именно RSAES-PKCS1-v1_5, а не OAEP. Анализатор справедливо считает
            // PKCS#1 v1.5 устаревшим (Bleichenbacher), но здесь выбора нет: схема прибита к
            // формату ClientKeyExchange в RFC 5246 §7.4.7.1, и OAEP сервер попросту не поймёт.
            // Смягчение по RFC — сервер обязан при ошибке расшифровки продолжать со СЛУЧАЙНЫМ
            // premaster, а не отвечать отличимой ошибкой; это его сторона, не наша.
#pragma warning disable S5542 // Encryption algorithms should be used with secure mode and padding scheme
            var encrypted = rsa.Encrypt(premaster, RSAEncryptionPadding.Pkcs1);
#pragma warning restore S5542

            // ★ Двухбайтовая длина шифротекста в TLS 1.0+ ПРИСУТСТВУЕТ (её нет только в SSLv3).
            // Забыть её — получить от сервера decrypt_error без единого пояснения.
            var body = new byte[2 + encrypted.Length];

            unchecked
            {
                body[0] = (byte)(encrypted.Length >> 8);
                body[1] = (byte)encrypted.Length;
            }

            encrypted.CopyTo(body.AsSpan(2));

            var cke = BuildHandshake(TlsHandshakeType.ClientKeyExchange, body);
            transcript.Append(cke.Span);

            premasterSecret = premaster.AsSpan().ToArray();
            return cke;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(premaster);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask SendClientKeyExchangeCcsFinishedAsync(CancellationToken cancellationToken)
    {
        // ★ Развилка обязана быть ПЕРЕД созданием эфемерного ключа. При обмене на RSA группа не
        // согласуется вовсе (ServerKeyExchange не приходит), chosenGroup остаётся нулём — а нуля
        // среди значений NamedGroup нет, и EnsureEcdheForGroup честно падал бы на «Группа не
        // поддержана». Выглядело бы это как отказ в поддержке кривой там, где кривых нет вовсе.
        if (KeyExchangeKind is Tls12KeyExchange.Ecdhe)
        {
            // Перекрёстная проверка с RFC 5246 §7.4.3: у эфемерного обмена ServerKeyExchange
            // обязателен. Без неё сервер, «забывший» это сообщение, получил бы premaster,
            // посчитанный на НУЛЕВЫХ полях, — и ошибка вылезла бы только на Finished.
            if (!serverKeyExchangeReceived)
                throw new InvalidOperationException("Набор требует ServerKeyExchange, а сервер его не прислал (RFC 5246 §7.4.3)");

            EnsureEcdheForGroup();

            // Ключ обмена готов, если создан подходящий для группы: для кривых NIST это объект
            // платформы, для X25519 — собственная пара. Требовать здесь именно платформенный значило
            // бы отвергать X25519, который платформа как раз и не поддерживает.
            if (chosenGroup is NamedGroup.X25519 ? x25519Private is null : ecdhe is null)
                throw new InvalidOperationException("Эфемерный ключ для согласованной группы не создан");
        }

        // (1) при запросе клиентского сертификата — пустой Certificate
        if (serverRequestedClientCert)
        {
            Span<byte> emptyList = stackalloc byte[3];
            var certMsg = BuildHandshake(TlsHandshakeType.Certificate, emptyList);
            transcript.Append(certMsg.Span);
            await SendPlainRecordAsync(TlsContentType.Handshake, certMsg, cancellationToken).ConfigureAwait(false);
        }

        // (2) ClientKeyExchange (формирование зависит от группы; см. пункт 2 ниже)
        var cke = BuildClientKeyExchangeBodyAndAppend(); // кладёт в transcript сам
        await SendPlainRecordAsync(TlsContentType.Handshake, cke, cancellationToken).ConfigureAwait(false);

        // (3) Теперь, когда в транскрипте есть клиентские сообщения, считаем EMS/master и key_block
        DeriveSecretsTls12AfterClientMessagesAppended();

        // (4) CCS
        var ccs = new byte[1]; ccs[0] = 1;
        await SendPlainRecordAsync(TlsContentType.ChangeCipherSpec, ccs, cancellationToken).ConfigureAwait(false);

        // (5) Finished (уже шифрованный, т.к. ключи готовы)
        Span<byte> verify = stackalloc byte[12];
        ComputeVerifyData(verify, forServer: false);
        var fin = BuildHandshake(TlsHandshakeType.Finished, verify);
        transcript.Append(fin.Span);
        await SendTls12RecordAsync(TlsContentType.Handshake, fin, cancellationToken).ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask ReceiveServerCcsAndFinishedAsync(CancellationToken cancellationToken)
    {
        // TLS 1.2 сервер может прислать plaintext NewSessionTicket перед CCS.
        while (true)
        {
            var (hdr, payload) = await ReadRecordAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (hdr.ContentType is TlsContentType.ChangeCipherSpec)
                {
                    if (hdr.Length is not 1 || payload[0] is not 1)
                        throw new InvalidOperationException("Ожидался корректный ChangeCipherSpec от сервера");

                    break;
                }

                if (hdr.ContentType is not TlsContentType.Handshake)
                    throw new InvalidOperationException("Ожидался ChangeCipherSpec или Handshake от сервера");

                HandleServerHandshakeBeforeFinished(payload.AsSpan(0, hdr.Length));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(payload, clearArray: true);
            }
        }

        // Finished (encrypted Handshake)
        var fin = await ReceiveTls12RecordAsync(TlsContentType.Handshake, cancellationToken).ConfigureAwait(false);

        try
        {
            var span = fin.Span;

            if (span.Length < 4 || (TlsHandshakeType)span[0] is not TlsHandshakeType.Finished)
                throw new InvalidOperationException("Ожидался Handshake Finished");

            var blen = (span[1] << 16) | (span[2] << 8) | span[3];
            var body = span.Slice(4, blen);

            Span<byte> expected = stackalloc byte[12];
            ComputeVerifyData(expected, forServer: true);
            if (!body.SequenceEqual(expected)) throw new CryptographicException("Server Finished verify_data не совпал");

            transcript.Append(span); // завершить транскрипт
        }
        finally
        {
            fin.Dispose();
        }
    }

    private void HandleServerHandshakeBeforeFinished(ReadOnlySpan<byte> payload)
    {
        while (payload.Length > 0)
        {
            if (payload.Length < 4) throw new InvalidOperationException("Handshake record too short before Finished");

            var type = (TlsHandshakeType)payload[0];
            var msgLen = (payload[1] << 16) | (payload[2] << 8) | payload[3];
            var totalLen = 4 + msgLen;

            if (totalLen > payload.Length)
                throw new InvalidOperationException("Fragmented handshake before Finished не поддержан");

            var msg = payload[..totalLen];
            if (type is not TlsHandshakeType.NewSessionTicket)
                throw new InvalidOperationException($"Неожиданное Handshake сообщение перед Finished: {type}");

            transcript.Append(msg);
            ParseNewSessionTicketBody(msg[4..]);
            payload = payload[totalLen..];
        }
    }

    private void ParseNewSessionTicketBody(ReadOnlySpan<byte> body)
    {
        if (body.Length < 6) throw new InvalidOperationException("NST too short (TLS 1.2)");

        var life = (uint)((body[0] << 24) | (body[1] << 16) | (body[2] << 8) | body[3]);
        var ticketLength = (body[4] << 8) | body[5];
        if (6 + ticketLength > body.Length) throw new InvalidOperationException("NST ticket length invalid");

        var ticket = new byte[ticketLength];
        body.Slice(6, ticketLength).CopyTo(ticket);

        SessionTicketLifetimeHint = life;
        SessionTicket = ticket;
    }

    /// <summary>
    /// Читает 0..N NewSessionTicket (TLS 1.2) сразу после Finished сервера.
    /// Прекращает чтение, как только встречает не-Handshake или Handshake отличного от NewSessionTicket.
    /// Никаких лишних аллокаций: тело тикета копируем в минимально необходимый буфер.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask ReadOptionalNewSessionTicketsAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var (hdr, payload) = await ReadRecordAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (hdr.ContentType is not TlsContentType.Handshake) return; // вышли на другой тип — как в браузерах

                var plain = ArrayPool<byte>.Shared.Rent(MaxPlaintextForRecord(hdr.Length));

                try
                {
                    if (!TryUnprotectRecord(TlsContentType.Handshake, payload.AsSpan(0, hdr.Length), plain, out var plainLen))
                        throw new CryptographicException(RecordNotAuthentic);

                    var span = plain.AsSpan(0, plainLen);
                    if (span.Length < 4) throw new InvalidOperationException("Handshake record too short");

                    var type = (TlsHandshakeType)span[0];
                    if (type is not TlsHandshakeType.NewSessionTicket) return; // это другой handshake — выходим, как браузер

                    var msgLen = (span[1] << 16) | (span[2] << 8) | span[3];
                    var body = span.Slice(4, msgLen); // тело NewSessionTicket (TLS 1.2)

                    if (msgLen < 6) throw new InvalidOperationException("NST too short (TLS 1.2)");

                    var life = (uint)((body[0] << 24) | (body[1] << 16) | (body[2] << 8) | body[3]);
                    var tLen = (body[4] << 8) | body[5];
                    if (6 + tLen > body.Length) throw new InvalidOperationException("NST ticket length invalid");

                    // Сохраняем ровно тикет и lifetime_hint.
                    var ticket = new byte[tLen];
                    body.Slice(6, tLen).CopyTo(ticket);

                    SessionTicketLifetimeHint = life;
                    SessionTicket = ticket;

                    // Сохраняем NST (по необходимости) — тело лежит после 3-байтовой длины.
                    //transcript.Append(span[..(4 + msgLen)]);  // не знаю нужно ли это
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(payload);
                    ArrayPool<byte>.Shared.Return(plain, clearArray: true);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(payload);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void HandleCertificate(ReadOnlySpan<byte> body)
    {
        if (string.IsNullOrEmpty(sniHost)) throw new InvalidOperationException("SNI (server_name) отсутствует");

        ParseCertificate(body);
        ValidateServerCertificate(serverCerts!, sniHost);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void HandleCertificateStatus(ReadOnlySpan<byte> body)
    {
        // struct {
        //   CertificateStatusType status_type;   // 1 байт: 1 = ocsp
        //   opaque response<1..2^24-1>;         // 3-байтовая длина + данные
        // } CertificateStatus (TLS 1.2)
        if (body.Length < 4) throw new InvalidOperationException("CertificateStatus слишком короток");

        var statusType = body[0];
        var rLen = (body[1] << 16) | (body[2] << 8) | body[3];

        if (statusType is not 1) return; // только OCSP
        if (4 + rLen > body.Length) throw new InvalidOperationException("Некорректная длина OCSP stapling");

        StapledOcspResponse = body.Slice(4, rLen).ToArray();
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void Dispose(bool disposing)
    {
        // Транскрипт держит пуловские буферы — освобождаем вместе с потоком.
        if (disposing) transcript.Dispose();

        if (disposing)
        {
            if (masterSecret is not null)
            {
                CryptographicOperations.ZeroMemory(masterSecret);
                masterSecret = default;
            }

            if (clientWriteKey is not null)
            {
                CryptographicOperations.ZeroMemory(clientWriteKey);
                clientWriteKey = default;
            }

            if (serverWriteKey is not null)
            {
                CryptographicOperations.ZeroMemory(serverWriteKey);
                serverWriteKey = default;
            }

            if (clientWriteIvSalt is not null)
            {
                CryptographicOperations.ZeroMemory(clientWriteIvSalt);
                clientWriteIvSalt = default;
            }

            if (serverWriteIvSalt is not null)
            {
                CryptographicOperations.ZeroMemory(serverWriteIvSalt);
                serverWriteIvSalt = default;
            }

            // MAC-ключи CBC — такой же секрет, как ключи шифрования: имея их, можно подделать
            // запись целиком. Забыть их здесь значило бы оставить их в куче до сборки мусора.
            if (clientWriteMacKey is not null)
            {
                CryptographicOperations.ZeroMemory(clientWriteMacKey);
                clientWriteMacKey = default;
            }

            if (serverWriteMacKey is not null)
            {
                CryptographicOperations.ZeroMemory(serverWriteMacKey);
                serverWriteMacKey = default;
            }

            if (serverCerts is not null)
            {
                foreach (var c in serverCerts) c.Dispose();
                serverCerts = default;
            }

            serverLeaf?.Dispose();
            ecdhe?.Dispose();
            aeadWrite12?.Dispose();
            aeadRead12?.Dispose();
            cbcWrite12?.Dispose();
            cbcRead12?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override int BuildClientHello(Span<byte> destination)
    {
        // Порядок берётся один раз: пересборка приветствия обязана дать то же сообщение.
        orderedExtensions ??= Settings.PermuteExtensions
            ? ClientHelloExtensionPermutation.Apply(Settings.Extensions, Settings.PermutationAnchors)
            : Settings.Extensions;

        var builder = ClientHelloBuilder.Create()
            .WithCipherSuites(Settings.CipherSuites)
            .WithExtensions(orderedExtensions)
            .WithSessionIdPolicy(Settings.SessionIdPolicy);

        var clientHello = builder.Build();
        if (clientHello.Length > destination.Length) throw new InvalidOperationException("Буфер мал для ClientHello");

        // Извлечь client_random (после: record(5) + handshake(4) + legacy_version(2) = 11 байт). Билдер кладёт random сразу после legacy_version, так что 32 байта random начинаются по смещению 11
        clientHello.CopyTo(destination);

        // client_random начинается на смещении 5 (record hdr) + 4 (hs hdr) + 2 (legacy_version) = 11
        var cr = new byte[32];
        destination.Slice(11, 32).CopyTo(cr);
        clientRandom = cr;
        var recordLen = (destination[3] << 8) | destination[4];
        sniHost = GetServerName(orderedExtensions);
        transcript.Append(destination.Slice(5, recordLen)); // 5 = размер record hdr
        ClientHelloBuilder.Return(builder);
        return clientHello.Length;
    }

    private static string? GetServerName(IEnumerable<Extensions.ITlsExtension> extensions)
    {
        foreach (var extension in extensions)
        {
            if (extension is Extensions.ServerNameTlsExtension serverName && !string.IsNullOrWhiteSpace(serverName.HostName))
                return serverName.HostName;
        }

        return null;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override ValueTask<bool> OnHandshakeRecordAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var span = payload.Span;
        byte[]? combinedBuffer = null;

        if (handshakeFragmentLength > 0)
        {
            combinedBuffer = ArrayPool<byte>.Shared.Rent(handshakeFragmentLength + span.Length);
            handshakeFragmentBuffer.AsSpan(0, handshakeFragmentLength).CopyTo(combinedBuffer);
            span.CopyTo(combinedBuffer.AsSpan(handshakeFragmentLength));
            span = combinedBuffer.AsSpan(0, handshakeFragmentLength + span.Length);
            ClearHandshakeFragmentBuffer();
        }

        try
        {
            while (span.Length >= 4)
            {
                var type = (TlsHandshakeType)span[0];
                var len = (span[1] << 16) | (span[2] << 8) | span[3];
                var messageLength = 4 + len;
                lastHandshakeType = type;
                lastHandshakeLength = len;
                lastHandshakeAvailable = span.Length;

                if (messageLength > span.Length)
                {
                    StoreHandshakeFragment(span);
                    return ValueTask.FromResult(false);
                }

                var msg = span[..messageLength];
                var body = msg[4..];

                transcript.Append(msg); // важно для verify_data

                if (HandleHandshakeMessage(type, body))
                {
                    ClearHandshakeFragmentBuffer();
                    return ValueTask.FromResult(true);
                }

                span = span[messageLength..];
            }

            if (!span.IsEmpty)
                StoreHandshakeFragment(span);

            return ValueTask.FromResult(false);
        }
        finally
        {
            if (combinedBuffer is not null)
                ArrayPool<byte>.Shared.Return(combinedBuffer);
        }
    }

    private void StoreHandshakeFragment(ReadOnlySpan<byte> fragment)
    {
        ClearHandshakeFragmentBuffer();

        handshakeFragmentBuffer = ArrayPool<byte>.Shared.Rent(fragment.Length);
        fragment.CopyTo(handshakeFragmentBuffer);
        handshakeFragmentLength = fragment.Length;
    }

    private void ClearHandshakeFragmentBuffer()
    {
        if (handshakeFragmentBuffer is not null)
        {
            ArrayPool<byte>.Shared.Return(handshakeFragmentBuffer, clearArray: true);
            handshakeFragmentBuffer = null;
        }

        handshakeFragmentLength = 0;
    }

    /// <summary>
    /// Обработчик одного Handshake-сообщения сервера (TLS 1.2).
    /// Возвращает true, если достигнут конец «первого полёта» (ServerHelloDone).
    /// В TLS 1.3 будет переопределён и/или расширен для EncryptedExtensions, Cert*, Finished и т.п.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual bool HandleHandshakeMessage(TlsHandshakeType type, ReadOnlySpan<byte> body)
    {
        switch (type)
        {
            case TlsHandshakeType.ServerHello:
                ParseServerHello(body);
                return default;

            case TlsHandshakeType.Certificate:
                HandleCertificate(body);
                return default;

            case TlsHandshakeType.ServerKeyExchange:
                // При обмене ключами на RSA этого сообщения быть не должно (RFC 5246 §7.4.3).
                // Разбирать его «на всякий случай» нельзя: его тело описывает параметры кривой,
                // и попытка вычитать их из чего-то другого даст либо мусорную группу, либо
                // исключение о неподдержанной кривой — то есть уведёт диагностику в сторону.
                if (KeyExchangeKind is Tls12KeyExchange.Rsa)
                    throw new InvalidOperationException($"Набор 0x{chosenSuite:X4} не предусматривает ServerKeyExchange (RFC 5246 §7.4.3)");

                ParseServerKeyExchange(body);
                serverKeyExchangeReceived = true;
                return default;

            case TlsHandshakeType.CertificateRequest:
                serverRequestedClientCert = true; // браузер: ответ пустым Certificate
                return default;

            case (TlsHandshakeType)22: // CertificateStatus (OCSP stapling)
                HandleCertificateStatus(body);
                return default;

            case TlsHandshakeType.ServerHelloDone:
                return true;

            default:
                throw new NotSupportedException($"Handshake {type} не поддерживается в TLS 1.2");
        }
    }

    /// <summary>
    /// Формирует explicit IV (если требуется), nonce и AAD для записи записи TLS.
    /// По умолчанию — модель TLS 1.2 AEAD (GCM/ChaCha20):
    /// nonce = clientWriteIvSalt(4) || explicitIv(8),
    /// AAD = seq(8) || contentType(1) || version(2=0x0303) || length(2).
    /// Для TLS 1.3 будет переопределено: иной nonce и AAD, иная упаковка record.
    /// </summary>
    /// <param name="type">Тип содержимого записи.</param>
    /// <param name="plainLen">Длина открытых данных.</param>
    /// <param name="seq">Счётчик записи (big-endian в AAD).</param>
    /// <param name="explicitIvDest">Куда писать 8-байтовый explicit IV (TLS 1.2). Для TLS 1.3 должен быть пустым.</param>
    /// <param name="nonceDest">Куда писать итоговый nonce для AEAD.</param>
    /// <param name="aadDest">Куда писать AAD (13 байт для TLS 1.2 AEAD).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void BuildAadAndNonceForWrite(TlsContentType type, int plainLen, ulong seq, Span<byte> explicitIvDest, Span<byte> nonceDest, Span<byte> aadDest)
    {
        if (IsCbcSuite)
        {
            // У CBC нет nonce вовсе, а явный вектор — случайный и создаётся при шифровании записи,
            // а не выводится из счётчика. Здесь остаются только тринадцать байт ниже: для CBC они
            // служат ЗАГОЛОВКОМ MAC (RFC 5246 §6.2.3.1), а не дополнительными данными AEAD.
            // Собирать их вторым методом было бы ошибкой: расхождение между отправкой и приёмом
            // проявилось бы как сплошной провал MAC, то есть как «сервер шлёт битые записи».
        }
        else if (IsChaChaSuite)
        {
            // RFC 7905: nonce = IV(12) XOR seq, дополненный нулями слева. Явного IV в записи нет,
            // поэтому explicitIvDest здесь пуст.
            BuildChaChaNonce(clientWriteIvSalt, seq, nonceDest);
        }
        else
        {
            // TLS 1.2: explicit_iv = seq (big-endian). Для единообразия — через помощник.
            WriteSeq(seq, explicitIvDest); // 8 байт

            // nonce = salt(4) + explicit_iv(8)
            nonceDest.Clear();
            clientWriteIvSalt.CopyTo(nonceDest[..4]);
            explicitIvDest.CopyTo(nonceDest[4..]);
        }

        // AAD = seq(8) | type(1) | 0x0303(2) | length(2)
        aadDest.Clear();
        WriteSeq(seq, aadDest[..8]);
        aadDest[8] = (byte)type;
        aadDest[9] = 0x03;
        aadDest[10] = 0x03;

        // Длина записи занимает два байта, и разбиение числа на разряды — не потеря данных.
        // Для фреймворка включена проверка переполнения, из-за чего эта запись бросала исключение
        // на ЛЮБОЙ длине больше 255 байт: TLS 1.2 не мог отправить ни одного реального запроса.
        unchecked
        {
            aadDest[11] = (byte)(plainLen >> 8);
            aadDest[12] = (byte)plainLen;
        }
    }

    /// <summary>
    /// Формирует nonce и AAD для чтения записи TLS.
    /// По умолчанию — модель TLS 1.2 AEAD с explicit IV в записи.
    /// </summary>
    /// <param name="type">Ожидаемый тип содержимого.</param>
    /// <param name="plainLen">Длина открытых данных (после снятия тега).</param>
    /// <param name="seq">Счётчик чтения (big-endian).</param>
    /// <param name="explicitIv">8-байтовый explicit IV из записи.</param>
    /// <param name="nonceDest">Куда писать nonce.</param>
    /// <param name="aadDest">Куда писать AAD.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void BuildAadAndNonceForRead(TlsContentType type, int plainLen, ulong seq, ReadOnlySpan<byte> explicitIv, Span<byte> nonceDest, Span<byte> aadDest)
    {
        if (IsCbcSuite)
        {
            // См. пояснение в BuildAadAndNonceForWrite: у CBC nonce нет, нужен только заголовок MAC.
        }
        else if (IsChaChaSuite)
        {
            BuildChaChaNonce(serverWriteIvSalt, seq, nonceDest);
        }
        else
        {
            nonceDest.Clear();
            serverWriteIvSalt.CopyTo(nonceDest[..4]);
            explicitIv.CopyTo(nonceDest[4..]);
        }

        aadDest.Clear();
        WriteSeq(seq, aadDest[..8]);
        aadDest[8] = (byte)type;
        aadDest[9] = 0x03;
        aadDest[10] = 0x03;

        // См. пояснение в BuildAadAndNonceForWrite: разбиение длины на два байта.
        unchecked
        {
            aadDest[11] = (byte)(plainLen >> 8);
            aadDest[12] = (byte)plainLen;
        }
    }

    /// <summary>
    /// Обрабатывает post-handshake Handshake запись (TLS 1.2).
    /// Сейчас нам важен HelloRequest (renegotiation). Браузеры не renegotiate:
    /// отправляют warning alert no_renegotiation и продолжают работу.
    /// В TLS 1.3 будет переопределено: других post-handshake сообщений нет.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual async ValueTask HandlePostHandshakeRecordAsync(ReadOnlyMemory<byte> encryptedHandshake, CancellationToken cancellationToken)
    {
        var plain = ArrayPool<byte>.Shared.Rent(MaxPlaintextForRecord(encryptedHandshake.Length));

        try
        {
            if (!TryUnprotectRecord(TlsContentType.Handshake, encryptedHandshake.Span, plain, out var plainLen))
                throw new CryptographicException(RecordNotAuthentic);

            var span = plain.AsSpan(0, plainLen);
            if (span.Length < 4) throw new InvalidOperationException("Handshake record too short");

            var type = (TlsHandshakeType)span[0];
            var len = (span[1] << 16) | (span[2] << 8) | span[3];
            if (4 + len > span.Length) throw new InvalidOperationException("Повреждённый Handshake фрейм");

            await OnPostHandshakeHandshakeMessageAsync(type, plain.AsMemory(4, len), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(plain, clearArray: true);
        }
    }

    /// <summary>
    /// Пользовательский хук обработки post-handshake Handshake (TLS 1.2).
    /// По умолчанию обрабатываем только HelloRequest (renegotiation) — как браузеры.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual async ValueTask OnPostHandshakeHandshakeMessageAsync(TlsHandshakeType type, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        if (type is 0) // HelloRequest
        {
            await SendAlertAsync(TlsAlertLevel.Warning, TlsAlertDescription.NoRenegotiation, cancellationToken).ConfigureAwait(false);
        }

        // иные post-handshake сообщения для TLS 1.2 нам не нужны
    }

    /// <summary>
    /// Разбор и обработка Alert (post-handshake).
    /// Возвращает действие: Continue/Eof/Fatal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual PostAlertAction HandleAlertRecord(ReadOnlySpan<byte> encryptedAlert)
    {
        var capacity = MaxPlaintextForRecord(encryptedAlert.Length);

        scoped Span<byte> plain;
        byte[]? rented = null;

        try
        {
            if (capacity <= 256)
            {
                plain = stackalloc byte[capacity];
            }
            else
            {
                rented = ArrayPool<byte>.Shared.Rent(capacity);
                plain = rented.AsSpan(0, capacity);
            }

            if (!TryUnprotectRecord(TlsContentType.Alert, encryptedAlert, plain, out var plainLen))
                return PostAlertAction.Fatal;

            if (plainLen < 2) return PostAlertAction.Fatal;

            var level = (TlsAlertLevel)plain[0];
            var desc = (TlsAlertDescription)plain[1];

            if (level is TlsAlertLevel.Warning && desc is TlsAlertDescription.CloseNotify) return PostAlertAction.Eof;
            if (level is TlsAlertLevel.Warning) return PostAlertAction.Continue;

            return PostAlertAction.Fatal;
        }
        finally
        {
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }

    /// <summary>
    /// Отправляет Alert (TLS 1.2) с текущими ключами. Без выделений в GC:
    /// тело alert (2 байта) берём из ArrayPool.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual async ValueTask SendAlertAsync(TlsAlertLevel level, TlsAlertDescription description, CancellationToken cancellationToken)
    {
        var buf = ArrayPool<byte>.Shared.Rent(2);

        try
        {
            buf[0] = (byte)level;
            buf[1] = (byte)description;

            await SendTls12RecordAsync(
                TlsContentType.Alert,
                buf.AsMemory(0, 2),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf, clearArray: true);
        }
    }

    /// <summary>
    /// Расшифровывает ApplicationData из payload и копирует в пользовательский буфер.
    /// При нехватке места создаёт внутренний cache (без лишних аллокаций — ArrayPool).
    /// Возвращает количество скопированных байт.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int DecryptAppDataAndCopyTo(ReadOnlyMemory<byte> payloadMem, int recordLen, Memory<byte> userBuffer)
    {
        var payload = payloadMem.Span;

        // Форма записи зависит от набора: у ChaCha20 явного вектора инициализации нет, у CBC он
        // шестнадцатибайтовый и случайный, а длина открытых данных заранее неизвестна.
        var plain = ArrayPool<byte>.Shared.Rent(MaxPlaintextForRecord(recordLen));

        try
        {
            if (!TryUnprotectRecord(TlsContentType.ApplicationData, payload[..recordLen], plain, out var plainLen))
                throw new CryptographicException(RecordNotAuthentic);

            var copy = Math.Min(plainLen, userBuffer.Length);
            plain.AsSpan(0, copy).CopyTo(userBuffer.Span);

            if (plainLen > copy)
            {
                decCache = plain;        // отложенная отдача (без копий)
                decPos = copy;
                decRemaining = plainLen - copy;
                // Возврат массива — когда decRemaining станет 0.
                return copy;
            }
            else
            {
                ArrayPool<byte>.Shared.Return(plain, clearArray: true);
                return copy;
            }
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(plain, clearArray: true);
            throw;
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override async ValueTask HandshakeAsync(CancellationToken cancellationToken)
    {
        // ClientHello
        var chBuf = ArrayPool<byte>.Shared.Rent(4096);

        try
        {
            var chSize = BuildClientHello(chBuf.AsSpan(0, 4096));
            if (chSize <= 0) throw new InvalidOperationException("ClientHello не сформирован");

            await WriteTransportAsync(chBuf.AsMemory(0, chSize), cancellationToken).ConfigureAwait(false);
            state = State.ClientHelloSent;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chBuf);
        }

        // Первый полёт сервера до ServerHelloDone
        state = State.AwaitServerFlight;
        var done = false;

        while (!done)
        {
            var (hdr, payload) = await ReadRecordAsync(cancellationToken).ConfigureAwait(false);
            lastRecordType = (byte)hdr.ContentType;
            lastRecordVersion = hdr.LegacyVersion;
            lastRecordLength = hdr.Length;
            lastPayloadByte0 = hdr.Length > 0 ? payload[0] : (byte)0;
            lastPayloadByte1 = hdr.Length > 1 ? payload[1] : (byte)0;
            lastPayloadByte2 = hdr.Length > 2 ? payload[2] : (byte)0;
            lastPayloadByte3 = hdr.Length > 3 ? payload[3] : (byte)0;

            try
            {
                if (hdr.ContentType is TlsContentType.Handshake)
                {
                    done = await OnHandshakeRecordAsync(payload.AsMemory(0, hdr.Length), cancellationToken).ConfigureAwait(false);
                }
                else if (hdr.ContentType is TlsContentType.Alert)
                {
                    // ★ Оповещение УРОВНЯ ПРЕДУПРЕЖДЕНИЯ рукопожатие не прерывает (RFC 5246,
                    // §7.2): сторона вправе его пропустить и продолжить. Мы же роняли соединение
                    // на любом, включая безобидное unrecognized_name — его сервер шлёт, когда имя
                    // из SNI ему незнакомо, но обслужить он всё равно готов.
                    //
                    // Найдено массовым прогоном: так терялся dunkindonuts.com, который отдаётся
                    // любому другому клиенту. Тот же разбор есть и в пути TLS 1.3.
                    var level = hdr.Length >= 1 ? payload[0] : (byte)2;
                    var description = hdr.Length >= 2 ? payload[1] : (byte)0;

                    if (level is not 1 || description == (byte)TlsAlertDescription.CloseNotify)
                    {
                        throw new InvalidOperationException($"Во время рукопожатия получен TLS alert: {DescribeAlert(payload.AsSpan(0, hdr.Length))}");
                    }
                }
                else
                {
                    throw new InvalidOperationException("Неожиданный тип записи");
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(payload);
            }
        }

        // ClientKeyExchange + CCS + Finished
        await SendClientKeyExchangeCcsFinishedAsync(cancellationToken).ConfigureAwait(false);

        // CCS + Finished сервера
        await ReceiveServerCcsAndFinishedAsync(cancellationToken).ConfigureAwait(false);

        state = State.Ready;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty) return 0;

        // 1) Сначала отдаём то, что уже лежит в decCache
        if (decRemaining > 0 && decCache is not null)
        {
            var n = Math.Min(decRemaining, buffer.Length);
            decCache.AsSpan(decPos, n).CopyTo(buffer.Span);
            decPos += n;
            decRemaining -= n;

            if (decRemaining is 0)
            {
                ArrayPool<byte>.Shared.Return(decCache, clearArray: true);
                decCache = null;
                decPos = 0;
            }

            return n;
        }

        // 2) Читаем записи, пока не встретим ApplicationData
        while (true)
        {
            var (hdr, payload) = await ReadRecordAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                switch (hdr.ContentType)
                {
                    case TlsContentType.ApplicationData: return DecryptAppDataAndCopyTo(payload.AsMemory(0, hdr.Length), hdr.Length, buffer);

                    case TlsContentType.Handshake:
                        {
                            // --- post-handshake: HelloRequest и др. ---
                            await HandlePostHandshakeRecordAsync(payload.AsMemory(0, hdr.Length), cancellationToken).ConfigureAwait(false);
                            // Ничего не отдаём пользователю, продолжаем читать следующую запись
                            break;
                        }

                    case TlsContentType.Alert:
                        {
                            // --- корректно обработать close_notify/прочие алерты ---
                            var action = HandleAlertRecord(payload.AsSpan(0, hdr.Length));
                            if (action is PostAlertAction.Eof) return 0; // close_notify → EOF
                            if (action is PostAlertAction.Continue) break; // читаем дальше
                            throw new InvalidOperationException("Получен фатальный alert от сервера");
                        }

                    case TlsContentType.ChangeCipherSpec:
                        {
                            // Специфические middlebox’ы могут присылать CCS внезапно — браузеры игнорируют
                            break;
                        }

                    default:
                        throw new InvalidOperationException("Неожиданный тип записи после рукопожатия");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload.AsSpan());
                ArrayPool<byte>.Shared.Return(payload, clearArray: true);
            }
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (state is not State.Ready) throw new InvalidOperationException("Handshake не завершён");

        var mem = buffer;
        const int MaxPlain = 16_384;

        while (!mem.IsEmpty)
        {
            var chunk = mem[..Math.Min(mem.Length, MaxPlain)];
            await SendTls12RecordAsync(TlsContentType.ApplicationData, chunk, cancellationToken).ConfigureAwait(false);
            mem = mem[chunk.Length..];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlyMemory<byte> BuildHandshake(TlsHandshakeType type, ReadOnlySpan<byte> body)
    {
        var mem = new byte[4 + body.Length];

        mem[0] = (byte)type;
        mem[1] = (byte)((body.Length >> 16) & 0xFF);
        mem[2] = (byte)((body.Length >> 8) & 0xFF);
        mem[3] = (byte)(body.Length & 0xFF);

        body.CopyTo(mem.AsSpan(4));
        return mem;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteSeq(ulong seq, Span<byte> dst)
    {
        // Разбиение 64-битного счётчика на восемь разрядов. Без unchecked проверка переполнения
        // срабатывала бы уже на 256-й записи соединения — то есть на первом же ответе средних
        // размеров, и выглядело бы это как случайный сбой посреди обмена.
        unchecked
        {
            dst[0] = (byte)(seq >> 56);
            dst[1] = (byte)(seq >> 48);
            dst[2] = (byte)(seq >> 40);
            dst[3] = (byte)(seq >> 32);
            dst[4] = (byte)(seq >> 24);
            dst[5] = (byte)(seq >> 16);
            dst[6] = (byte)(seq >> 8);
            dst[7] = (byte)seq;
        }
    }

    /// <summary>
    /// PRF для TLS 1.2 (на базе HMAC с SHA-256/384, без старых MD5/SHA-1).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Tls12Prf(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> label, ReadOnlySpan<byte> seed, Span<byte> output, HashAlgorithmName hash)
    {
        if (output.IsEmpty) return;

        // label||seed — константный для всей функции
        var lsLen = label.Length + seed.Length;

        // Самая частая ветка — короткие label/seed (≤ 512 байт): безопасно держим на стеке.
        // Иначе переходим на ArrayPool.
        var usePool = lsLen > 512;
        byte[]? pooledLs = default;

        var ls = usePool
            ? (pooledLs = ArrayPool<byte>.Shared.Rent(lsLen)).AsSpan(0, lsLen)
            : stackalloc byte[lsLen];

        label.CopyTo(ls[..label.Length]);
        seed.CopyTo(ls[label.Length..]);

        var hLen = hash switch
        {
            var hh when hh == HashAlgorithmName.SHA256 => 32,
            var hh when hh == HashAlgorithmName.SHA384 => 48,
            var hh when hh == HashAlgorithmName.SHA512 => 64,
            _ => throw new NotSupportedException("TLS 1.2 PRF поддерживает SHA-256/384/512")
        };

        // A(i) — всегда фиксированной длины = hLen → можно выделить один буфер.
        Span<byte> A = stackalloc byte[hLen];

        // Буфер для HMAC(secret, A || (label||seed)) — длина постоянна: hLen + lsLen.
        // Вынесен из цикла (CA2014).
        var usePoolComb = (hLen + lsLen) > 1024; // порог — чисто предосторожность
        byte[]? pooledComb = null;

        var aPlusLs = usePoolComb
            ? (pooledComb = ArrayPool<byte>.Shared.Rent(hLen + lsLen)).AsSpan(0, hLen + lsLen)
            : stackalloc byte[hLen + lsLen];

        // A(1) = HMAC(secret, label||seed)
        HmacHashData(secret, ls, A, hash);

        var written = 0;
        Span<byte> h = stackalloc byte[hLen];

        while (written < output.Length)
        {
            // HMAC(secret, A || label||seed)
            A.CopyTo(aPlusLs[..hLen]);
            ls.CopyTo(aPlusLs[hLen..]);

            h.Clear();
            HmacHashData(secret, aPlusLs, h, hash);

            var take = Math.Min(hLen, output.Length - written);
            h[..take].CopyTo(output[written..(written + take)]);
            written += take;

            // A = HMAC(secret, A)
            HmacHashData(secret, A, A, hash);
        }

        if (pooledComb is not null) ArrayPool<byte>.Shared.Return(pooledComb, clearArray: true);
        if (pooledLs is not null) ArrayPool<byte>.Shared.Return(pooledLs, clearArray: true);
    }

    /// <summary>
    /// Статический HMAC без аллокаций объекта на горячем пути.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HmacHashData(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data, Span<byte> dest, HashAlgorithmName hash)
    {
        if (hash == HashAlgorithmName.SHA256)
            HMACSHA256.HashData(key, data, dest);
        else if (hash == HashAlgorithmName.SHA384)
            HMACSHA384.HashData(key, data, dest);
        else if (hash == HashAlgorithmName.SHA512)
            HMACSHA512.HashData(key, data, dest);
        else
            throw new NotSupportedException("HMAC: только SHA-256/384/512");
    }

    /// <summary>
    /// HKDF-Extract.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void HkdfExtract(ReadOnlySpan<byte> salt, ReadOnlySpan<byte> ikm, Span<byte> prk, HashAlgorithmName alg)
    {
        if (alg == HashAlgorithmName.SHA256)
            HMACSHA256.HashData(salt, ikm, prk);
        else
            HMACSHA384.HashData(salt, ikm, prk);
    }

    /// <summary>
    /// HKDF-Expand (один или несколько блоков).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void HkdfExpand(ReadOnlySpan<byte> prk, ReadOnlySpan<byte> info, Span<byte> output, HashAlgorithmName hash)
    {
        if (output.IsEmpty) return;

        var hLen = hash switch
        {
            var h when h == HashAlgorithmName.SHA256 => 32,
            var h when h == HashAlgorithmName.SHA384 => 48,
            var h when h == HashAlgorithmName.SHA512 => 64,
            _ => throw new NotSupportedException("HKDF: только SHA-256/384/512")
        };

        // Максимальная длина блока входа HMAC в цикле: T(n-1)(hLen) + info + 1.
        var bufLen = hLen + info.Length + 1;
        var usePool = bufLen > 1024;
        byte[]? pooled = default;

        var buf = usePool
            ? (pooled = ArrayPool<byte>.Shared.Rent(bufLen)).AsSpan(0, bufLen)
            : stackalloc byte[bufLen];

        // T(n-1) — держим отдельно и переиспользуем
        Span<byte> tPrev = stackalloc byte[hLen];
        Span<byte> t = stackalloc byte[hLen];
        var tPrevLen = 0;

        var written = 0;
        byte counter = 1;

        while (written < output.Length)
        {
            var off = 0;

            if (tPrevLen > 0)
            {
                tPrev[..tPrevLen].CopyTo(buf[..tPrevLen]);
                off += tPrevLen;
            }

            info.CopyTo(buf[off..(off + info.Length)]);
            off += info.Length;

            buf[off] = counter;
            off += 1;

            // HMAC(PRK, concat)
            t.Clear();
            HmacHashData(prk, buf[..off], t, hash);

            var take = Math.Min(hLen, output.Length - written);
            t[..take].CopyTo(output[written..(written + take)]);
            written += take;

            t.CopyTo(tPrev);
            tPrevLen = hLen;
            counter++;

            // Защита от переполнения счётчика (теоретически output может быть очень длинным)
            if (counter is 0) throw new CryptographicException("HKDF counter overflow");
        }

        if (pooled is not null) ArrayPool<byte>.Shared.Return(pooled, clearArray: true);
    }

    /// <summary>
    /// Строит nonce для наборов ChaCha20-Poly1305.
    /// </summary>
    /// <param name="iv">Фиксированный вектор инициализации длиной 12 байт.</param>
    /// <param name="seq">Счётчик записей.</param>
    /// <param name="destination">Куда писать nonce.</param>
    /// <remarks>
    /// По RFC 7905 счётчик дополняется нулями слева до длины IV и складывается с ним по модулю
    /// два. Именно эта схема принята и в TLS 1.3, поэтому запись ChaCha20 в TLS 1.2 не несёт
    /// явного вектора инициализации.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void BuildChaChaNonce(ReadOnlySpan<byte> iv, ulong seq, Span<byte> destination)
    {
        iv[..12].CopyTo(destination);

        // Счётчик занимает младшие восемь байт: складываем побайтно, начиная с конца.
        unchecked
        {
            for (var index = 0; index < 8; index++)
                destination[11 - index] ^= (byte)(seq >> (index * 8));
        }
    }

    private static bool IsDowngrade(ReadOnlySpan<byte> rnd)
    {
        // Значения «сентинелов» по RFC8446 (TLS 1.3), оба варианта допустимо проверять
        ReadOnlySpan<byte> tls12 = [0x44, 0x4F, 0x57, 0x4E, 0x47, 0x52, 0x44, 0x01]; // "DOWNGRD\x01"
        ReadOnlySpan<byte> tls11 = [0x44, 0x4F, 0x57, 0x4E, 0x47, 0x52, 0x44, 0x00]; // "DOWNGRD\x00"
        var tail = rnd[^8..];
        return tail.SequenceEqual(tls12) || tail.SequenceEqual(tls11);
    }

    /// <summary>
    /// Валидация цепочки X.509 как в браузерах: SAN/CN против SNI, EKU=ServerAuth, revocation по политике.
    /// Минимум для мимикрии: построить цепочку и проверить имя хоста.
    /// </summary>
    private void ValidateServerCertificate(List<X509Certificate2> certs, string sniHost)
    {
        if (certs is null || certs.Count is 0) throw new CryptographicException("Сертификат сервера отсутствует");

        var leaf = certs[0];

        using var chain = new X509Chain();
        // Политика максимально близкая к браузерам; подстройте из вашего Settings/Profile.
        chain.ChainPolicy.RevocationMode = Settings.CheckCertificateRevocationList ? X509RevocationMode.Online : X509RevocationMode.NoCheck;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.DisableCertificateDownloads = false;          // AIA разрешены (как в браузерах)
        chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(10);

        // EKU: Server Authentication
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));

        // Прокладываем промежуточные в ExtraStore
        for (var i = 1; i < certs.Count; i++) chain.ChainPolicy.ExtraStore.Add(certs[i]);

        var chainBuilt = chain.Build(leaf);
        var nameMatches = HostnameMatches(leaf, sniHost);
        var sslPolicyErrors = SslPolicyErrors.None;

        if (!chainBuilt)
            sslPolicyErrors |= SslPolicyErrors.RemoteCertificateChainErrors;

        if (!nameMatches)
            sslPolicyErrors |= SslPolicyErrors.RemoteCertificateNameMismatch;

        var callback = Settings.ServerCertificateValidationCallback;
        if (callback is not null)
        {
            if (!callback(leaf, chain, sslPolicyErrors))
                throw new CryptographicException("Пользовательская валидация сертификата сервера отклонила соединение");

            return;
        }

        if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateChainErrors) is not 0)
            throw new CryptographicException("Не удалось построить цепочку сертификатов сервера");

        if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateNameMismatch) is not 0)
        {
            throw new CryptographicException(
                $"Имя хоста не соответствует сертификату сервера: ожидалось '{sniHost}', в сертификате {DescribeCertificateNames(leaf)}");
        }
    }

    /// <summary>
    /// Проверяет соответствие имени хоста сертификату.
    /// </summary>
    /// <param name="cert">Сертификат сервера.</param>
    /// <param name="host">Запрошенное имя.</param>
    /// <returns><see langword="true"/>, если имя соответствует.</returns>
    /// <remarks>
    /// Проверка делегируется общей: своя копия здесь уже была, и она отличалась от общей —
    /// брала из SAN только первое имя. Две реализации одного правила расходятся всегда, а
    /// расхождение в проверке имени сертификата — это либо отказ на исправном сервере, либо
    /// принятый чужой сертификат.
    /// </remarks>
    private static bool HostnameMatches(X509Certificate2 cert, string host)
        => ServerCertificateVerifier.HostnameMatches(cert, host);

    /// <summary>Перечисляет имена сертификата для сообщения об ошибке.</summary>
    /// <param name="certificate">Сертификат.</param>
    /// <returns>Имена через запятую.</returns>
    private static string DescribeCertificateNames(X509Certificate2 certificate)
        => ServerCertificateVerifier.DescribeCertificateNames(certificate);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool WildcardMatch(string pattern, string host)
    {
        // Поддерживаем только левый wildcard: *.example.com
        if (pattern.Length > 2 && pattern[0] is '*' && pattern[1] is '.')
        {
            var suffix = pattern.AsSpan(1); // ".example.com"

            return host.AsSpan().EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                   && host.AsSpan().IndexOf('.') > 0; // не матчим корень
        }

        return string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryReadServerHelloExtensions(ReadOnlySpan<byte> exts, out bool ems, out bool encryptThenMac, out ReadOnlyMemory<byte> alpn)
    {
        ems = false;
        encryptThenMac = false;
        alpn = default;
        var epos = 0;

        while (epos + 4 <= exts.Length)
        {
            var et = (ushort)((exts[epos] << 8) | exts[epos + 1]);
            var el = (ushort)((exts[epos + 2] << 8) | exts[epos + 3]);
            epos += 4;

            if (epos + el > exts.Length) return false;

            var data = exts.Slice(epos, el);

            if (et is 0x0010) // ALPN
            {
                if (data.Length >= 3)
                {
                    var total = (data[0] << 8) | data[1];
                    if (total + 2 == data.Length)
                    {
                        var nLen = data[2];
                        if (nLen > 0 && 3 + nLen == data.Length)
                            alpn = data.Slice(3, nLen).ToArray();
                    }
                }
            }
            else if (et is 0x0017)
            {
                ems = true;
            }
            else if (et is 0x0016) // encrypt_then_mac (RFC 7366)
            {
                encryptThenMac = true;
            }

            epos += el;
        }

        return true;
    }

    /// <summary>
    /// Реализован ли набор в этом клиенте.
    /// </summary>
    /// <param name="suite">Код набора.</param>
    /// <returns><see langword="true"/>, если набор можно довести до конца.</returns>
    /// <remarks>
    /// Единственный источник — общая таблица. Объявлять набор поддержанным раньше, чем он
    /// действительно работает, ХУЖЕ отказа: вместо внятного «не реализован» получится непонятная
    /// поломка посреди обмена, которую придётся ловить по дампу.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsImplementedSuite(ushort suite)
        => Tls12CipherSuiteParameters.TryGet(suite, out var parameters) && parameters.IsAvailable;

    /// <summary>
    /// Предлагали ли мы этот набор в ClientHello.
    /// </summary>
    /// <param name="suite">Код набора.</param>
    /// <param name="offered">Список из настроек — ровно тот, что ушёл на провод.</param>
    /// <returns><see langword="true"/>, если набор был в предложении.</returns>
    /// <remarks>
    /// Проверка обязательна и отдельно от «реализовано»: расширяя список реализованного, легко
    /// случайно разрешить серверу выбрать НЕПРЕДЛАГАВШИЙСЯ набор — а это уже не мимикрия, а дыра.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsOfferedSuite(ushort suite, IEnumerable<CipherSuite> offered)
    {
        foreach (var s in offered) if ((ushort)s == suite) return true;

        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static NamedGroup MapServerNamedGroup(ushort named) => named switch
    {
        0x0017 => NamedGroup.Secp256r1,
        0x0018 => NamedGroup.Secp384r1,
        0x0019 => NamedGroup.Secp521r1,
        0x001D => NamedGroup.X25519,
        _ => throw new NotSupportedException($"Группа 0x{named:X4} не поддержана в TLS 1.2")
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UnpackServerPublicKey(NamedGroup group, ReadOnlySpan<byte> pub)
    {
        switch (group)
        {
            case NamedGroup.Secp256r1:
                if (pub.Length != 65 || pub[0] != 0x04) throw new NotSupportedException("Ожидается ECPoint P-256");
                serverEcQx = pub.Slice(1, 32).ToArray();
                serverEcQy = pub.Slice(33, 32).ToArray();
                break;

            case NamedGroup.Secp384r1:
                if (pub.Length != 97 || pub[0] != 0x04) throw new NotSupportedException("Ожидается ECPoint P-384");
                serverEcQx = pub.Slice(1, 48).ToArray();
                serverEcQy = pub.Slice(49, 48).ToArray();
                break;

            case NamedGroup.Secp521r1:
                if (pub.Length != 133 || pub[0] != 0x04) throw new NotSupportedException("Ожидается ECPoint P-521");
                serverEcQx = pub.Slice(1, 66).ToArray();
                serverEcQy = pub.Slice(67, 66).ToArray();
                break;

            case NamedGroup.X25519:
                if (pub.Length != 32) throw new NotSupportedException("X25519: ожидается 32 байта");
                serverX25519Pub = pub.ToArray();
                break;

            default:
                throw new NotSupportedException("Группа не поддержана");
        }
    }
}