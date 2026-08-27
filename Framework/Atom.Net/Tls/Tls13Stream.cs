using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Atom.Net.Tls;

/// <summary>
/// Реализация клиентского потока TLS 1.3 (RFC 8446).
/// </summary>
/// <remarks>
/// Наследуется от <see cref="TlsStream"/>, а не от <see cref="Tls12Stream"/>, намеренно. Общего у
/// версий только каркас записей, который и живёт в базовом типе; всё остальное различается
/// принципиально: в 1.3 нет ServerKeyExchange и ClientKeyExchange, ключи выводятся расписанием
/// HKDF, а тип содержимого записи прячется ВНУТРЬ шифротекста, тогда как заголовок служит
/// связанными данными. Наследование от 1.2 связало бы рабочий путь, которым сейчас идёт весь
/// трафик, с новым кодом — цена ошибки была бы несоразмерна выигрышу от переиспользования
/// полутора десятков строк.
///
/// Порядок рукопожатия: ClientHello → ServerHello (ключи рукопожатия) → EncryptedExtensions →
/// Certificate → CertificateVerify → Finished сервера → Finished клиента → прикладные ключи.
/// </remarks>
/// <param name="stream">Сетевой поток.</param>
/// <param name="settings">Настройки TLS.</param>
public sealed class Tls13Stream([NotNull] NetworkStream stream, in TlsSettings settings) : TlsStream(stream, settings)
{
    /// <summary>Размер IV записи для AEAD, применяемых в TLS 1.3.</summary>
    private const int RecordIvLength = 12;

    /// <summary>Максимальный размер полезной нагрузки одной записи.</summary>
    private const int MaxPlaintextLength = 16384;

    /// <summary>
    /// Движок рукопожатия. Уровень записей ничего о нём не знает, кроме секретов трафика.
    /// </summary>
    private readonly Tls13ClientHandshake handshake = new(settings);

    /// <summary>
    /// Текущий прикладной секрет сервера; обновляется по KeyUpdate.
    /// </summary>
    /// <remarks>
    /// Хранится здесь, а не в движке рукопожатия: смена ключа — событие уровня записей, оно
    /// происходит уже после рукопожатия и к транскрипту отношения не имеет.
    /// </remarks>
    private byte[]? serverTrafficSecret;

    private IAeadCipher? handshakeRead;
    private IAeadCipher? handshakeWrite;
    private byte[]? handshakeIvRead;
    private byte[]? handshakeIvWrite;

    private ulong readSequence;
    private ulong writeSequence;
    private bool handshakeComplete;

    private byte[]? pendingPlaintext;
    private int pendingOffset;
    private int pendingLength;

    /// <summary>Отправлено ли ПЕРВОЕ приветствие этого соединения.</summary>
    /// <remarks>
    /// Различие между первым приветствием и повторным после HelloRetryRequest наблюдаемо на
    /// уровне записей: послабление по версии в заголовке дано только первому. Подробности — в
    /// <see cref="BuildClientHello"/>.
    /// </remarks>
    private bool initialClientHelloSent;

    /// <summary>Отправлена ли пустышка change_cipher_spec совместимости.</summary>
    /// <remarks>Она разрешена ровно одна на соединение; см. <see cref="SendCompatibilityCcsAsync"/>.</remarks>
    private bool compatibilityCcsSent;

    /// <summary>
    /// В TLS 1.3 заголовок записи всегда несёт 0x0303 после первой записи, а сама версия
    /// согласуется расширением supported_versions.
    /// </summary>
    protected override ushort RecordLayerVersion => 0x0303;

    /// <summary>
    /// Цепочка сертификатов сервера в порядке получения.
    /// </summary>
    /// <inheritdoc cref="Tls13ClientHandshake.ServerCertificates"/>
    public IReadOnlyList<X509Certificate2> ServerCertificates => handshake.ServerCertificates;

    /// <inheritdoc cref="Tls13ClientHandshake.ResumptionAccepted"/>
    public bool ResumptionAccepted => handshake.ResumptionAccepted;

    /// <summary>
    /// 0-RTT данные для отправки сразу после ClientHello; задаётся до <see cref="HandshakeAsync"/>.
    /// </summary>
    /// <remarks>
    /// Данные уйдут под ранними ключами только если сервер объявил в билете max_early_data.
    /// Принял ли их сервер — смотрите <see cref="EarlyDataAccepted"/> после рукопожатия: без
    /// подтверждения ранние данные сервером выброшены, и запрос обязан быть переотправлен.
    /// </remarks>
    public ReadOnlyMemory<byte>? EarlyData { get; set; }

    /// <inheritdoc cref="Tls13ClientHandshake.EarlyDataAccepted"/>
    public bool EarlyDataAccepted => handshake.EarlyDataAccepted;

    /// <inheritdoc cref="Tls13ClientHandshake.NegotiatedGroup"/>
    public NamedGroup NegotiatedGroup => handshake.NegotiatedGroup;

    /// <inheritdoc/>
    /// <remarks>
    /// ★ Версия в заголовке записи у ПЕРВОГО приветствия и у повторного РАЗНАЯ, и это не мелочь
    /// оформления. RFC 8446, §5.1: legacy_record_version обязана быть 0x0303 во всех записях,
    /// кроме начального ClientHello, где допускается ещё и 0x0301 — послабление ради посредников,
    /// которые обрывают незнакомую им версию в самом первом пакете. Ключевое слово здесь
    /// «начального»: на повторное приветствие после HelloRetryRequest послабление НЕ
    /// распространяется.
    ///
    /// Мы же ставили 0x0301 обоим, и строгий сервер отвечал на второе приветствие фатальным
    /// protocol_version (70) — то есть ронял рукопожатие, успешное во всём остальном.
    ///
    /// Поймать это было тяжело ровно потому, что HelloRetryRequest наступает не от нашего
    /// приветствия, а от ВКУСОВ СЕРВЕРА: он приходит, когда сервер предпочёл группу, которую мы
    /// объявили, но долю для неё заранее не отправили. Профиль Chrome шлёт доли для гибрида и
    /// X25519, и сервер, требующий P-256, отправляет повтор; профиль Firefox шлёт третью долю
    /// именно для P-256 и повтора не получает — поэтому отказ выглядел свойством ПРОФИЛЯ, хотя
    /// профиль тут ни при чём. Сверка приветствия с браузерным тоже ничего не давала: первое
    /// сообщение у нас и у Chrome совпадает байт в байт, а расходится второе, которого в обычном
    /// обмене просто не бывает.
    ///
    /// Замер: и curl (OpenSSL), и настоящий Chrome отправляют 0x0301 в первом приветствии и
    /// 0x0303 во втором — снято приёмником на своём же обмене.
    /// </remarks>
    protected override int BuildClientHello(Span<byte> destination)
    {
        var message = handshake.BuildClientHello();
        var total = 5 + message.Length;

        if (total > destination.Length) throw new InvalidOperationException("Буфер мал для ClientHello");

        // Уровень записей добавляет свой заголовок к готовому сообщению рукопожатия.
        var version = initialClientHelloSent ? RecordLayerVersion : (ushort)0x0301;

        destination[0] = 0x16;
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(1, 2), version);
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(3, 2), (ushort)message.Length);
        message.CopyTo(destination[5..]);

        return total;
    }

    /// <inheritdoc/>
    public override async ValueTask HandshakeAsync(CancellationToken cancellationToken)
    {
        if (clientKeyShareMissing()) throw new InvalidOperationException("Для TLS 1.3 в настройках обязан быть key_share");

        await SendClientHelloAsync(cancellationToken).ConfigureAwait(false);
        await SendEarlyDataAsync(cancellationToken).ConfigureAwait(false);
        await ReceiveServerHelloAsync(cancellationToken).ConfigureAwait(false);

        // ★ Сервер вправе попросить повторить приветствие с другой группой — это HelloRetryRequest
        // (RFC 8446, §4.1.4). Случай не частый, но и не выдуманный: он наступает, когда сервер
        // предпочитает группу, которую мы объявили в supported_groups, но долю для неё заранее не
        // отправили — например P-384 или P-521.
        //
        // Повтор допускается РОВНО ОДИН: второй подряд означает, что сервер водит нас по кругу.
        if (handshake.NeedsRetry)
        {
            // Пустышка совместимости идёт ПЕРЕД повторным приветствием — ровно так её ставит
            // браузер, и это единственное место, где она может оказаться при HelloRetryRequest:
            // второй полёт клиента начинается здесь.
            await SendCompatibilityCcsAsync(cancellationToken).ConfigureAwait(false);
            await SendClientHelloAsync(cancellationToken).ConfigureAwait(false);
            await ReceiveServerHelloAsync(cancellationToken).ConfigureAwait(false);
        }

        await ReceiveEncryptedFlightAsync(cancellationToken).ConfigureAwait(false);

        // Результат согласования ALPN живёт в движке рукопожатия — он и разбирает
        // EncryptedExtensions. Поднимаем его на уровень потока: вызывающая сторона выбирает по
        // нему класс соединения, и потеря значения означает, что HTTP/2 не будет выбран НИКОГДА,
        // а обмен по согласованному h2 пойдёт разбором HTTP/1.1 — то есть развалится.
        NegotiatedProtocol = handshake.NegotiatedProtocol;

        // Если повтора не было, второй полёт клиента — это как раз зашифрованный Finished, и
        // пустышка совместимости встаёт перед ним. Метод сам следит, чтобы запись ушла один раз.
        await SendCompatibilityCcsAsync(cancellationToken).ConfigureAwait(false);
        await SendClientFinishedAsync(cancellationToken).ConfigureAwait(false);

        // Resumption master secret снимается ПОСЛЕ Finished клиента, пока транскрипт жив: билеты
        // сервер пришлёт уже после рукопожатия, и без этого секрета PSK из них не вывести.
        handshake.CaptureResumptionMasterSecret();

        // Рукопожатие завершено: транскрипт больше не нужен и освобождает свои буферы.
        handshake.ReleaseTranscript();

        ActivateApplicationKeys();
        handshakeComplete = true;

        bool clientKeyShareMissing()
        {
            foreach (var extension in Settings.Extensions)
            {
                if (extension is Extensions.KeyShareTlsExtension) return false;
            }

            return true;
        }
    }

    /// <inheritdoc/>
    protected override ValueTask<bool> OnHandshakeRecordAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Рукопожатие 1.3 разбирается пошагово в HandshakeAsync: сообщения приходят строго
        // определёнными полётами, и общий диспетчер здесь только запутал бы поток управления.
        return ValueTask.FromResult(true);
    }

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty) return 0;
        if (!handshakeComplete) throw new InvalidOperationException("Чтение до завершения рукопожатия");

        while (true)
        {
            if (pendingLength > 0)
            {
                var take = Math.Min(pendingLength, buffer.Length);
                pendingPlaintext.AsSpan(pendingOffset, take).CopyTo(buffer.Span);
                pendingOffset += take;
                pendingLength -= take;

                if (pendingLength == 0) ReleasePending();
                return take;
            }

            var (contentType, plaintext, length) = await ReadProtectedRecordAsync(cancellationToken).ConfigureAwait(false);

            switch (contentType)
            {
                case TlsContentType.None:
                    return 0;

                case TlsContentType.ApplicationData:
                    // ★ Запись нулевой длины разрешена (RFC 8446, §5.4) и используется как
                    // заполнение или поддержание активности. Прежде её буфер оставался у нас:
                    // накопитель перезаписывался на следующем витке, и арендованный массив
                    // терялся мимо пула — тихая утечка ровно на тех соединениях, что живут долго.
                    if (length is 0)
                    {
                        ArrayPool<byte>.Shared.Return(plaintext);
                        continue;
                    }

                    pendingPlaintext = plaintext;
                    pendingOffset = 0;
                    pendingLength = length;
                    continue;

                case TlsContentType.Handshake:
                    // После рукопожатия сервер вправе прислать NewSessionTicket и KeyUpdate.
                    // Билеты нам сейчас не нужны, но игнорировать запись нельзя — её надо снять с
                    // провода, иначе поток рассинхронизируется.
                    HandlePostHandshake(plaintext.AsSpan(0, length));
                    ArrayPool<byte>.Shared.Return(plaintext);
                    continue;

                case TlsContentType.Alert:
                    // Уровень и описание вынимаем ДО возврата буфера в пул: после Return его
                    // содержимое принадлежит уже не нам.
                    var alert = DescribeAlert(plaintext.AsSpan(0, length));
                    var closeNotify = length >= 2 && plaintext[1] == (byte)TlsAlertDescription.CloseNotify;
                    ArrayPool<byte>.Shared.Return(plaintext);
                    if (closeNotify) return 0;
                    throw new InvalidOperationException($"Получен TLS alert: {alert}");

                default:
                    ArrayPool<byte>.Shared.Return(plaintext);
                    throw new InvalidOperationException($"Неожиданный тип записи {contentType}");
            }
        }
    }

    /// <inheritdoc/>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!handshakeComplete) throw new InvalidOperationException("Запись до завершения рукопожатия");

        var rest = buffer;
        while (!rest.IsEmpty)
        {
            var chunk = rest[..Math.Min(rest.Length, MaxPlaintextLength)];
            await SendProtectedRecordAsync(TlsContentType.ApplicationData, chunk, cancellationToken).ConfigureAwait(false);
            rest = rest[chunk.Length..];
        }
    }

    private async ValueTask SendClientHelloAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MaxPlaintextLength);

        try
        {
            var size = BuildClientHello(buffer.AsSpan(0, MaxPlaintextLength));
            if (size <= 0) throw new InvalidOperationException("ClientHello не сформирован");

            await WriteTransportAsync(buffer.AsMemory(0, size), cancellationToken).ConfigureAwait(false);

            // Признак ставится ПОСЛЕ успешной отправки: до неё начальное приветствие ещё не
            // состоялось, и повтор попытки обязан снова получить послабление по версии.
            initialClientHelloSent = true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Отправляет пустышку change_cipher_spec перед вторым полётом клиента.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <remarks>
    /// Это «режим совместимости с посредниками» (RFC 8446, §D.4). В TLS 1.3 смены шифра как
    /// события нет, и запись не значит ничего: она существует затем, чтобы обмен ВЫГЛЯДЕЛ для
    /// промежуточного оборудования как привычный ему TLS 1.2, где после приветствий всегда идёт
    /// change_cipher_spec. Спецификация разрешает ровно одну такую запись — непосредственно перед
    /// вторым полётом клиента, то есть либо перед повторным ClientHello, либо перед зашифрованным
    /// Finished.
    ///
    /// ★ Мы не отправляли её НИКОГДА, и это расхождение с браузером наблюдаемо в первых же
    /// пакетах: замер приёмником показал, что и Chrome, и curl отправляют её на каждом
    /// соединении. Отсутствие ничего не ломало на серверах Google и Cloudflare — они и без неё
    /// работают, — поэтому пробел держался незамеченным, но клиент, у которого этой записи нет,
    /// отличим от заявленного браузера так же ясно, как по лишнему расширению.
    ///
    /// Условие — непустой legacy_session_id: именно им клиент объявляет, что идёт в режиме
    /// совместимости. Профили браузеров его заполняют всегда, а рукопожатие поверх QUIC —
    /// наоборот, обязано оставить пустым, и там запись была бы нарушением.
    /// </remarks>
    private ValueTask SendCompatibilityCcsAsync(CancellationToken cancellationToken)
    {
        if (compatibilityCcsSent || Settings.SessionIdPolicy is SessionIdPolicy.Empty) return ValueTask.CompletedTask;

        compatibilityCcsSent = true;

        return SendPlainRecordAsync(TlsContentType.ChangeCipherSpec, CompatibilityCcsPayload, cancellationToken);
    }

    /// <summary>Тело пустышки change_cipher_spec: единственное допустимое значение.</summary>
    private static ReadOnlyMemory<byte> CompatibilityCcsPayload { get; } = new byte[] { 0x01 };

    private async ValueTask ReceiveServerHelloAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var (header, payload) = await ReadRecordAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // ChangeCipherSpec в TLS 1.3 смысла не несёт и присылается только ради
                // совместимости с промежуточным оборудованием. После HelloRetryRequest сервер
                // ставит его ПЕРЕД вторым ServerHello — и запись эту надо молча пропустить, а не
                // принять за нарушение порядка.
                if (header.ContentType is TlsContentType.ChangeCipherSpec) continue;

                if (header.ContentType is TlsContentType.Alert)
                {
                    // ★ Оповещение УРОВНЯ ПРЕДУПРЕЖДЕНИЯ обрывом не является. Спецификация делит
                    // их на два уровня, и предупреждение (кроме close_notify) сторона вправе
                    // пропустить и продолжить рукопожатие — так и делают браузеры. Мы же роняли
                    // соединение на любом оповещении, включая безобидные вроде
                    // unrecognized_name, которое сервер шлёт, когда имя из SNI ему незнакомо, но
                    // обслужить он всё равно готов.
                    //
                    // Найдено массовым прогоном: так терялся, в частности, dunkindonuts.com —
                    // сервер отдаёт его любому другому клиенту.
                    var level = header.Length >= 1 ? payload[0] : (byte)2;
                    var description = header.Length >= 2 ? payload[1] : (byte)0;

                    if (level is 1 && description != (byte)TlsAlertDescription.CloseNotify) continue;

                    throw new InvalidOperationException($"Вместо ServerHello получен TLS alert: {DescribeAlert(payload.AsSpan(0, header.Length))}");
                }

                if (header.ContentType is not TlsContentType.Handshake) throw new InvalidOperationException("Ожидался ServerHello");

                handshake.ProcessServerHello(payload.AsSpan(0, header.Length));
                break;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(payload);
            }
        }

        // После HelloRetryRequest секретов ещё нет: их выведет ответ на ВТОРОЕ приветствие.
        // Попытка поставить ключи здесь оборвала бы рукопожатие ровно в тот момент, когда оно
        // идёт по совершенно штатному пути.
        if (!handshake.NeedsRetryPending) DeriveHandshakeKeys();
    }

    /// <summary>
    /// Превращает секреты рукопожатия в ключи уровня записей.
    /// </summary>
    private void DeriveHandshakeKeys()
    {
        if (handshake.ClientHandshakeSecret.IsEmpty || handshake.ServerHandshakeSecret.IsEmpty)
            throw new InvalidOperationException("Секреты рукопожатия не выведены");

        ApplyTrafficSecrets(handshake.ServerHandshakeSecret.Span, handshake.ClientHandshakeSecret.Span);
    }

    /// <summary>
    /// Ставит ключи чтения и записи, выведенные из пары секретов трафика.
    /// </summary>
    /// <param name="readSecret">Секрет, которым шифрует сервер.</param>
    /// <param name="writeSecret">Секрет, которым шифруем мы.</param>
    private void ApplyTrafficSecrets(ReadOnlySpan<byte> readSecret, ReadOnlySpan<byte> writeSecret)
    {
        var (readKey, readIv) = Tls13KeySchedule.DeriveRecordKeys(handshake.HashAlgorithm, readSecret, handshake.AeadKeyLength, RecordIvLength);
        var (writeKey, writeIv) = Tls13KeySchedule.DeriveRecordKeys(handshake.HashAlgorithm, writeSecret, handshake.AeadKeyLength, RecordIvLength);

        handshakeRead?.Dispose();
        handshakeWrite?.Dispose();

        handshakeRead = CreateAead(readKey);
        handshakeWrite = CreateAead(writeKey);
        handshakeIvRead = readIv;
        handshakeIvWrite = writeIv;

        readSequence = 0;
        writeSequence = 0;
    }

    private async ValueTask ReceiveEncryptedFlightAsync(CancellationToken cancellationToken)
    {
        var finished = false;

        while (!finished)
        {
            var (contentType, plaintext, length) = await ReadProtectedRecordAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // ChangeCipherSpec в 1.3 не несёт смысла и присылается только ради совместимости с
                // промежуточным оборудованием: его молча пропускаем, в транскрипт он не идёт.
                if (contentType is TlsContentType.ChangeCipherSpec) continue;
                if (contentType is TlsContentType.Alert)
                    throw new InvalidOperationException($"Во время рукопожатия получен TLS alert: {DescribeAlert(plaintext.AsSpan(0, length))}");
                if (contentType is not TlsContentType.Handshake) throw new InvalidOperationException("Ожидались сообщения рукопожатия");

                finished = handshake.ProcessHandshakeMessages(plaintext.AsSpan(0, length));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(plaintext);
            }
        }
    }

    /// <summary>
    /// Отправляет завершающий полёт клиента.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Задача отправки.</returns>
    /// <remarks>
    /// Когда сервер согласовал ALPS, перед Finished обязано уйти встречное EncryptedExtensions —
    /// иначе сервер сочтёт Finished неожиданным и оборвёт уже установленное соединение
    /// (см. <see cref="Tls13ClientHandshake.BuildClientEncryptedExtensions"/>).
    ///
    /// Обе части уезжают ОДНОЙ записью: так делает браузер, а размер записи наблюдаем снаружи.
    /// Порядок вызовов важен — EncryptedExtensions обязан попасть в транскрипт до того, как по
    /// нему посчитается verify_data.
    /// </remarks>
    private ValueTask SendClientFinishedAsync(CancellationToken cancellationToken)
    {
        // Порядок задан спецификацией и важен: сначала настройки ALPS, затем ответ на запрос
        // сертификата, и только потом Finished — verify_data считается по всему, что до него.
        // EOED попадает в транскрипт ПОСЛЕ полёта сервера и ДО Finished клиента — в этой
        // позиции его ожидает сервер при проверке verify_data.
        if (pendingEndOfEarlyData is not null)
        {
            handshake.AppendEndOfEarlyData(pendingEndOfEarlyData);
            pendingEndOfEarlyData = null;
        }

        var applicationSettings = handshake.BuildClientEncryptedExtensions();
        var certificate = handshake.BuildClientCertificate();
        var finished = handshake.BuildClientFinished();

        if (applicationSettings is null && certificate is null)
            return SendProtectedRecordAsync(TlsContentType.Handshake, finished, cancellationToken);

        var length = (applicationSettings?.Length ?? 0) + (certificate?.Length ?? 0) + finished.Length;
        var flight = new byte[length];
        var offset = 0;

        if (applicationSettings is not null)
        {
            applicationSettings.CopyTo(flight, offset);
            offset += applicationSettings.Length;
        }

        if (certificate is not null)
        {
            certificate.CopyTo(flight, offset);
            offset += certificate.Length;
        }

        finished.CopyTo(flight, offset);

        return SendProtectedRecordAsync(TlsContentType.Handshake, flight, cancellationToken);
    }

    /// <summary>
    /// Создаёт шифр записи под согласованный набор.
    /// </summary>
    /// <param name="key">Ключ трафика.</param>
    /// <returns>Готовый шифр.</returns>
    /// <remarks>
    /// Схема nonce у обоих наборов в TLS 1.3 одинаковая — сложение счётчика записей с
    /// фиксированным вектором по модулю два, — поэтому уровню записей достаточно подменить сам
    /// примитив, и ни чтение, ни запись править не приходится.
    /// </remarks>
    private IAeadCipher CreateAead(ReadOnlySpan<byte> key)
        => handshake.NegotiatedCipherSuite is CipherSuite.TLS_CHACHA20_POLY1305_SHA256
            ? new ChaCha20Poly1305Aead(key)
            : new AesGcmAead(key);

    private void ActivateApplicationKeys()
    {
        if (handshake.ClientApplicationSecret.IsEmpty || handshake.ServerApplicationSecret.IsEmpty)
            throw new InvalidOperationException("Прикладные секреты не выведены");

        serverTrafficSecret = handshake.ServerApplicationSecret.ToArray();
        ApplyTrafficSecrets(serverTrafficSecret, handshake.ClientApplicationSecret.Span);
    }

    private void HandlePostHandshake(ReadOnlySpan<byte> data)
    {
        var position = 0;

        while (position + 4 <= data.Length)
        {
            var type = (TlsHandshakeType)data[position];
            var length = (data[position + 1] << 16) | (data[position + 2] << 8) | data[position + 3];
            if (position + 4 + length > data.Length) break;

            // KeyUpdate обязывает сменить ключ чтения: иначе последующие записи не расшифруются.
            if ((byte)type is 24 && serverTrafficSecret is not null)
            {
                serverTrafficSecret = Tls13KeySchedule.DeriveNextTrafficSecret(handshake.HashAlgorithm, serverTrafficSecret);
                var (key, iv) = Tls13KeySchedule.DeriveRecordKeys(handshake.HashAlgorithm, serverTrafficSecret, handshake.AeadKeyLength, RecordIvLength);

                handshakeRead?.Dispose();
                handshakeRead = CreateAead(key);
                handshakeIvRead = iv;
                readSequence = 0;
            }

            // Билет сессии — предложение сервера возобновить соединение в следующий раз: PSK
            // выводится сразу, пока жив resumption master secret рукопожатия, и уходит наверх
            // подписчикам. Без билета ресумпции не бывает.
            if (type is TlsHandshakeType.NewSessionTicket && handshake.ResumptionMasterSecret.Length > 0)
            {
                try
                {
                    var ticket = ParseSessionTicket(data.Slice(position, 4 + length));
                    SessionTicketReceived?.Invoke(ticket);
                }
                catch (FormatException)
                {
                    // Чужой или обрезанный билет не повод рвать рабочее соединение: сервер,
                    // шлющий сообщение по правилам 1.2, упадёт здесь разбором, а обмен идёт.
                }
            }

            position += 4 + length;
        }
    }

    /// <summary>
    /// Разбирает NewSessionTicket и выводит PSK билета (RFC 8446, §4.6.1).
    /// </summary>
    /// <param name="message">Сообщение целиком, включая четырёхбайтовый заголовок.</param>
    /// <returns>Билет с готовым PSK.</returns>
    private Tls13SessionTicket ParseSessionTicket(ReadOnlySpan<byte> message)
    {
        var body = message[4..];

        var lifetime = BinaryPrimitives.ReadUInt32BigEndian(body);
        var ticketAgeAdd = BinaryPrimitives.ReadUInt32BigEndian(body[4..]);

        var position = 8;
        var nonceLength = body[position++];
        if (position + nonceLength + 2 > body.Length) throw new FormatException("Билет сессии обрезан");
        var nonce = body.Slice(position, nonceLength);
        position += nonceLength;

        var ticketLength = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(position, 2));
        position += 2;
        if (position + ticketLength > body.Length) throw new FormatException("Билет сессии обрезан");
        var ticket = body.Slice(position, ticketLength);
        position += ticketLength;

        // В расширениях билета нас интересует только early_data (0x002A): сколько байт 0-RTT
        // сервер готов принять по этому билету. Нет расширения — ранние данные не предлагать.
        var maxEarlyData = 0;
        if (position + 2 <= body.Length)
        {
            var extensionsLength = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(position, 2));
            var extEnd = Math.Min(position + 2 + extensionsLength, body.Length);
            var extPosition = position + 2;

            while (extPosition + 4 <= extEnd)
            {
                var extId = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(extPosition, 2));
                var extLength = BinaryPrimitives.ReadUInt16BigEndian(body.Slice(extPosition + 2, 2));
                extPosition += 4;

                if (extId == 0x002A && extLength >= 4 && extPosition + 4 <= body.Length)
                    maxEarlyData = (int)Math.Min((long)BinaryPrimitives.ReadUInt32BigEndian(body.Slice(extPosition, 4)), int.MaxValue);

                extPosition += extLength;
            }
        }

        var preSharedKey = Tls13KeySchedule.DeriveResumptionPsk(handshake.HashAlgorithm, handshake.ResumptionMasterSecret.Span, nonce);

        return new Tls13SessionTicket
        {
            Identity = ticket.ToArray(),
            PreSharedKey = preSharedKey,
            TicketAgeAdd = ticketAgeAdd,
            Hash = handshake.HashAlgorithm,
            Lifetime = TimeSpan.FromSeconds(lifetime),
            MaxEarlyData = maxEarlyData,
        };
    }

    /// <summary>
    /// Получен билет возобновления сессии от сервера.
    /// </summary>
    public event Action<Tls13SessionTicket>? SessionTicketReceived;

    private async ValueTask<(TlsContentType ContentType, byte[] Plaintext, int Length)> ReadProtectedRecordAsync(CancellationToken cancellationToken)
    {
        var (header, payload, endOfStream) = await TryReadRecordAsync(cancellationToken).ConfigureAwait(false);

        // Партнёр закрыл соединение на границе записи. Для прикладного чтения это штатный конец
        // потока, а не сбой: тело без объявленной длины именно так и заканчивается.
        if (endOfStream) return (TlsContentType.None, [], 0);

        try
        {
            // Незашифрованный ChangeCipherSpec допустим и после смены ключей — он идёт мимо AEAD.
            if (header.ContentType is TlsContentType.ChangeCipherSpec)
            {
                var copy = ArrayPool<byte>.Shared.Rent(Math.Max((int)header.Length, 1));
                payload.AsSpan(0, header.Length).CopyTo(copy);
                return (TlsContentType.ChangeCipherSpec, copy, header.Length);
            }

            if (handshakeRead is null || handshakeIvRead is null) throw new InvalidOperationException("Ключи чтения не установлены");

            var plaintext = ArrayPool<byte>.Shared.Rent(header.Length);

            try
            {
                Span<byte> nonce = stackalloc byte[RecordIvLength];
                BuildNonce(handshakeIvRead, readSequence, nonce);
                readSequence++;

                Span<byte> aad = stackalloc byte[5];
                header.Write(aad);

                if (!handshakeRead.TryDecrypt(nonce, aad, payload.AsSpan(0, header.Length), plaintext, out var written))
                    throw new InvalidOperationException("Не удалось расшифровать запись TLS 1.3");

                // TLSInnerPlaintext: content || content_type || zeros. Настоящий тип лежит в
                // последнем НЕнулевом байте, а хвост из нулей — это дополнение.
                var end = written - 1;
                while (end >= 0 && plaintext[end] == 0) end--;
                if (end < 0) throw new InvalidOperationException("Пустая запись TLS 1.3");

                return ((TlsContentType)plaintext[end], plaintext, end);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(plaintext);
                throw;
            }
        }
        finally
        {
            if (payload is not null) ArrayPool<byte>.Shared.Return(payload);
        }
    }

    private async ValueTask SendProtectedRecordAsync(TlsContentType contentType, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (handshakeWrite is null || handshakeIvWrite is null) throw new InvalidOperationException("Ключи записи не установлены");

        var innerLength = data.Length + 1;
        var recordLength = innerLength + handshakeWrite.TagSize;
        var buffer = ArrayPool<byte>.Shared.Rent(5 + recordLength);

        try
        {
            var header = new TlsRecordHeader(TlsContentType.ApplicationData, RecordLayerVersion, (ushort)recordLength);
            header.Write(buffer.AsSpan(0, 5));

            var inner = ArrayPool<byte>.Shared.Rent(innerLength);

            try
            {
                data.Span.CopyTo(inner);
                inner[data.Length] = (byte)contentType;

                Span<byte> nonce = stackalloc byte[RecordIvLength];
                BuildNonce(handshakeIvWrite, writeSequence, nonce);
                writeSequence++;

                if (!handshakeWrite.TryEncrypt(nonce, buffer.AsSpan(0, 5), inner.AsSpan(0, innerLength), buffer.AsSpan(5, recordLength), out _))
                    throw new InvalidOperationException("Не удалось зашифровать запись TLS 1.3");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(inner);
            }

            await WriteTransportAsync(buffer.AsMemory(0, 5 + recordLength), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Собирает nonce записи: IV, поксоренный с порядковым номером в младших байтах (RFC 8446, §5.3).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void BuildNonce(ReadOnlySpan<byte> iv, ulong sequence, Span<byte> destination)
    {
        iv.CopyTo(destination);

        Span<byte> sequenceBytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(sequenceBytes, sequence);

        var offset = destination.Length - 8;
        for (var index = 0; index < 8; index++) destination[offset + index] ^= sequenceBytes[index];
    }

    private void ReleasePending()
    {
        if (pendingPlaintext is null) return;

        ArrayPool<byte>.Shared.Return(pendingPlaintext);
        pendingPlaintext = null;
        pendingOffset = 0;
    }

    /// <summary>Тело warning close_notify для уведомления о закрытии (RFC 8446, §6).</summary>
    private static ReadOnlySpan<byte> CloseNotifyAlert => [1, 0];

    private IAeadCipher? earlyWrite;
    private byte[]? earlyWriteIv;
    private ulong earlySequence;
    private byte[]? pendingEndOfEarlyData;

    /// <summary>
    /// Отправляет 0-RTT данные под ранними ключами сразу после ClientHello (RFC 8446, §2.3).
    /// </summary>
    /// <remarks>
    /// Ключи выводятся из client_early_traffic_secret по транскрипту из ОДНОГО ClientHello.
    /// Внешний тип записи — application_data, внутренний — тоже: других типов в 0-RTT не бывает.
    /// Если сервер ранние данные отверг, он их выбросит — содержимое обязан переотправить тот,
    /// кто его передал.
    /// </remarks>
    private async ValueTask SendEarlyDataAsync(CancellationToken cancellationToken)
    {
        if (handshake.ClientEarlyTrafficSecret.IsEmpty || EarlyData is not { Length: > 0 }) return;

        var (key, iv) = Tls13KeySchedule.DeriveRecordKeys(handshake.HashAlgorithm, handshake.ClientEarlyTrafficSecret.Span, handshake.AeadKeyLength, RecordIvLength);
        earlyWrite?.Dispose();
        earlyWrite = CreateAead(key);
        earlyWriteIv = iv;
        earlySequence = 0;

        var rest = EarlyData.Value;
        while (!rest.IsEmpty)
        {
            var take = (int)Math.Min(rest.Length, MaxPlaintextLength);
            await SendEarlyRecordAsync(rest[..take], TlsContentType.ApplicationData, cancellationToken).ConfigureAwait(false);
            rest = rest[take..];
        }

        // EndOfEarlyData (RFC 8446, §4.5) замыкает ранние данные: шифруется РАННИМИ ключами,
        // в транскрипт НЕ входит. Без него сервер пытается прочитать наши CCS/Finished как
        // продолжение 0-RTT и рвёт соединение с bad_record_mac.
        var endOfEarlyData = new byte[] { (byte)TlsHandshakeType.EndOfEarlyData, 0, 0, 0 };
        await SendEarlyRecordAsync(endOfEarlyData, TlsContentType.Handshake, cancellationToken).ConfigureAwait(false);
        pendingEndOfEarlyData = endOfEarlyData;

        DisposeEarlyKeys();
    }

    private async ValueTask SendEarlyRecordAsync(ReadOnlyMemory<byte> data, TlsContentType innerType, CancellationToken cancellationToken)
    {
        if (earlyWrite is null || earlyWriteIv is null) throw new InvalidOperationException("Ранние ключи записи не установлены");

        var innerLength = data.Length + 1;
        var recordLength = innerLength + earlyWrite.TagSize;
        var buffer = ArrayPool<byte>.Shared.Rent(5 + recordLength);

        try
        {
            var header = new TlsRecordHeader(TlsContentType.ApplicationData, RecordLayerVersion, (ushort)recordLength);
            header.Write(buffer.AsSpan(0, 5));

            var inner = ArrayPool<byte>.Shared.Rent(innerLength);

            try
            {
                data.Span.CopyTo(inner);
                inner[data.Length] = (byte)innerType;

                Span<byte> nonce = stackalloc byte[RecordIvLength];
                BuildNonce(earlyWriteIv, earlySequence, nonce);
                earlySequence++;

                if (!earlyWrite.TryEncrypt(nonce, buffer.AsSpan(0, 5), inner.AsSpan(0, innerLength), buffer.AsSpan(5, recordLength), out _))
                    throw new InvalidOperationException("Не удалось зашифровать 0-RTT запись TLS 1.3");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(inner);
            }

            await WriteTransportAsync(buffer.AsMemory(0, 5 + recordLength), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void DisposeEarlyKeys()
    {
        earlyWrite?.Dispose();
        earlyWrite = null;
        earlyWriteIv = null;
        earlySequence = 0;
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        // ★ close_notify перед закрытием — браузерное поведение, а не вежливость: обрыв без него
        // сервер видит как незавершённую запись и, в отличие от реакции на браузер, помечает
        // соединение как ошибочное. Отправка best-effort: лучшее время — пока ключи живы.
        if (handshakeComplete)
        {
            try
            {
                await SendProtectedRecordAsync(TlsContentType.Alert, CloseNotifyAlert.ToArray(), default).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or System.Net.Sockets.SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // Соединение уже мертво — уведомлять нечем, и это не ошибка освобождения.
            }
        }

        ReleasePending();

        handshakeRead?.Dispose();
        handshakeWrite?.Dispose();

        handshake.Dispose();

        await base.DisposeAsync().ConfigureAwait(false);
    }
}
