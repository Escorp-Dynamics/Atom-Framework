using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Atom.Net.Tls;

/// <summary>
/// Защита записи TLS 1.2 в режиме CBC с отдельным HMAC (RFC 5246 §6.2.3.2), одно направление.
/// </summary>
/// <remarks>
/// Форма записи: <c lang="text">IV(16) || AES-CBC(content || MAC(20) || padding || padding_length(1))</c>,
/// где все байты дополнения равны <c lang="text">padding_length</c>, а сумма
/// <c lang="text">content + MAC + padding + 1</c> кратна длине блока.
///
/// ★ Почему этот класс существует отдельно, а не «ещё одним if» в каждом месте расшифровки.
/// В потоке TLS 1.2 запись расшифровывается из пяти разных мест (Finished, тикеты,
/// post-handshake, алерты, прикладные данные). Дописать CBC в каждое значило бы завести пять
/// копий проверки дополнения и метки. Копии расходятся всегда; расхождение именно в проверке
/// подлинности — это уже не неудобство, а дыра. Плюс требование единого пути отказа (см. ниже)
/// пятью путями отказа не выполняется по определению.
///
/// ★ Стойкость к замеру времени. Проверка дополнения и проверка метки обязаны занимать
/// одинаковое время и давать ОДИН И ТОТ ЖЕ отказ независимо от того, что именно не сошлось.
/// Разница здесь — классические Lucky13 и POODLE (RFC 7457 §2.2): различимые по времени или по
/// коду ошибки ветки превращают партнёра в оракул, по которому расшифровывается трафик.
/// Поэтому: ни одного раннего выхода по секретным данным, метка считается ВСЕГДА (даже когда
/// дополнение уже признано негодным), сравнение — только <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>,
/// наружу — единственное <see langword="false"/> без подробностей.
///
/// ★ Что закрыто НЕ полностью — честно. Число блоков сжатия SHA-1 зависит от длины content, а та
/// зависит от <c lang="text">padding_length</c>, то есть от секрета. Убрать эту зависимость до конца можно
/// лишь ручным управлением блоками сжатия (как <c lang="text">ssl3_cbc_digest_record</c> в OpenSSL);
/// <see cref="HMACSHA1"/> такого интерфейса не даёт. Здесь разница компенсируется холостым
/// дохешированием на недостающую длину — это выравнивает счёт блоков с точностью до одного, но не
/// является математическим доказательством. Смягчающее обстоятельство: мы КЛИЕНТ, а Lucky13
/// требует оракула, который атакующий может опрашивать миллионы раз, подставляя свои записи, —
/// у клиента такой позиции обычно нет. «Обычно» — не «никогда», поэтому остальные меры не
/// опциональны.
/// </remarks>
[SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "HMAC-SHA1 задан наборами шифров TLS 1.2 (…_CBC_SHA); выбор алгоритма делает сервер, не мы.")]
[SuppressMessage("Security", "CA5358:Review cipher mode usage with cryptography experts", Justification = "Режим CBC предписан RFC 5246 §6.2.3.2 для наборов …_CBC_SHA; разбор сделан стойким к замеру времени.")]
internal sealed class Tls12CbcRecordProtection : IDisposable
{
    /// <summary>Длина блока AES — она же длина явного вектора инициализации записи.</summary>
    internal const int BlockLength = Tls12CipherSuiteParameters.AesBlockLength;

    /// <summary>Длина метки HMAC-SHA1.</summary>
    internal const int MacLength = Tls12CipherSuiteParameters.Sha1MacLength;

    /// <summary>Смещение поля длины в тринадцатибайтовом заголовке MAC.</summary>
    private const int MacHeaderLengthOffset = 11;

    /// <summary>Длина заголовка MAC: seq(8) | type(1) | version(2) | length(2).</summary>
    internal const int MacHeaderLength = 13;

    /// <summary>Наименьшее осмысленное тело записи: метка, байт длины дополнения — и выравнивание.</summary>
    private const int MinCipherLength = 2 * BlockLength;

    /// <summary>Дополнение занимает не более 256 позиций, значит и проверять надо ровно столько.</summary>
    private const int MaxPaddingSpan = 256;

    private static readonly byte[] FillerZeroes = new byte[MaxPaddingSpan];

    private readonly Aes aes;
    private readonly byte[] macKey;

    /// <summary>
    /// Приёмник холостого хэша.
    /// </summary>
    /// <remarks>
    /// Существует затем, чтобы выравнивающее дохеширование не выглядело для JIT мёртвым кодом:
    /// выброшенный компилятором «холостой» вызов не выравнивает ничего, а восстановить его по
    /// симптому невозможно — разница во времени измеряется десятками наносекунд.
    /// </remarks>
    [SuppressMessage("Major Code Smell", "S4487:Unread private fields should be removed", Justification = "Приёмник холостого хэша: поле не читается намеренно, оно удерживает вычисление от устранения.")]
    private readonly byte[] fillerDigest = new byte[MacLength];

    /// <summary>
    /// Инициализирует защиту одного направления.
    /// </summary>
    /// <param name="encryptionKey">Ключ шифрования (16 или 32 байта).</param>
    /// <param name="macKeyBytes">Ключ HMAC-SHA1 (20 байт).</param>
    public Tls12CbcRecordProtection(ReadOnlySpan<byte> encryptionKey, ReadOnlySpan<byte> macKeyBytes)
    {
        // Режим и дополнение здесь НЕ выставляются свойствами объекта: EncryptCbc/DecryptCbc берут
        // их аргументом на каждый вызов, и указать их дважды значило бы завести второе место, где
        // задаётся одно и то же. Дополнение всегда None — его формирует и проверяет этот класс сам,
        // потому что автоматическая проверка PKCS#7 в .NET бросает исключение по неверному
        // дополнению, а различимый отказ по дополнению и есть уязвимость (см. описание класса).
        aes = Aes.Create();
        aes.Key = encryptionKey.ToArray();
        macKey = macKeyBytes.ToArray();
    }

    /// <summary>
    /// Возвращает полную длину записи для указанной длины открытых данных.
    /// </summary>
    /// <param name="plainLength">Длина открытых данных.</param>
    /// <returns>Длина тела записи (без пятибайтового заголовка).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ProtectedLength(int plainLength)
    {
        var body = plainLength + MacLength + 1;
        var padding = (BlockLength - (body & (BlockLength - 1))) & (BlockLength - 1);
        return BlockLength + body + padding;
    }

    /// <summary>
    /// Защищает одну запись.
    /// </summary>
    /// <param name="macHeader">Тринадцать байт seq|type|version|length — уже с длиной открытых данных.</param>
    /// <param name="plain">Открытые данные.</param>
    /// <param name="record">Буфер тела записи длиной не менее <see cref="ProtectedLength(int)"/>.</param>
    /// <returns>Сколько байт записано.</returns>
    public int Protect(ReadOnlySpan<byte> macHeader, ReadOnlySpan<byte> plain, Span<byte> record)
    {
        var total = ProtectedLength(plain.Length);
        var bodyLength = total - BlockLength;

        // ★ Явный вектор — криптографически СЛУЧАЙНЫЙ, а не счётчик и не производная от него.
        // Предсказуемый вектор режима CBC — это ровно дефект TLS 1.0, ради устранения которого
        // явный вектор в 1.1/1.2 и вводили (BEAST, CVE-2011-3389). Взять сюда seq было бы
        // «работающим» кодом, который тихо возвращает уязвимость десятилетней давности.
        RandomNumberGenerator.Fill(record[..BlockLength]);

        var scratch = ArrayPool<byte>.Shared.Rent(bodyLength);

        try
        {
            var body = scratch.AsSpan(0, bodyLength);

            // Порядок обязателен: MAC поверх ОТКРЫТОГО текста, затем дополнение, затем шифрование
            // (MAC-then-encrypt). Обратный порядок (encrypt-then-MAC) — отдельное расширение
            // 0x0016, которого мы не предлагаем; согласовать его сервер не вправе, и разбирать
            // записи иначе он не станет.
            plain.CopyTo(body);
            ComputeMac(macHeader, plain, body.Slice(plain.Length, MacLength));

            var paddingStart = plain.Length + MacLength;
            var paddingLength = bodyLength - paddingStart - 1;

            // Все байты дополнения равны его длине, включая замыкающий (RFC 5246 §6.2.3.2).
            body[paddingStart..].Fill((byte)paddingLength);

            var written = aes.EncryptCbc(body, record[..BlockLength], record.Slice(BlockLength, bodyLength), PaddingMode.None);
            if (written != bodyLength) throw new CryptographicException("CBC: неожиданная длина шифротекста");

            return total;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch, clearArray: true);
        }
    }

    /// <summary>
    /// Снимает защиту с одной записи.
    /// </summary>
    /// <param name="record">Тело записи: явный вектор и шифротекст.</param>
    /// <param name="macHeader">Тринадцать байт seq|type|version|length; поле длины будет дописано здесь.</param>
    /// <param name="plain">Буфер под открытые данные длиной не менее <c lang="text">record.Length - 16</c>.</param>
    /// <param name="plainLength">Длина открытых данных.</param>
    /// <returns><see langword="true"/>, если запись подлинна.</returns>
    /// <remarks>
    /// Единственный отказ на все причины — см. пояснение к классу. Вызывающая сторона обязана
    /// превращать <see langword="false"/> в один и тот же алерт <c lang="text">bad_record_mac</c>: различимые
    /// снаружи причины отказа и есть оракул.
    /// </remarks>
    public bool TryUnprotect(ReadOnlySpan<byte> record, Span<byte> macHeader, Span<byte> plain, out int plainLength)
    {
        plainLength = 0;

        // Публичные проверки: длина записи видна атакующему и без нас, ветвление по ней ничего не
        // выдаёт. Всё, что идёт дальше, ветвиться по содержимому уже не имеет права.
        var cipherLength = record.Length - BlockLength;

        if (cipherLength < MinCipherLength || (cipherLength & (BlockLength - 1)) is not 0) return false;
        if (plain.Length < cipherLength) return false;

        var target = plain[..cipherLength];

        try
        {
            if (aes.DecryptCbc(record[BlockLength..], record[..BlockLength], target, PaddingMode.None) != cipherLength)
                return false;
        }
        catch (CryptographicException)
        {
            return false;
        }

        // --- Проверка дополнения без единого раннего выхода -------------------------------------
        // Накопитель good держит младшие восемь бит взведёнными, пока всё сходится. Ни break, ни
        // if по секретным данным: именно ранний выход из этого цикла и делает Lucky13 измеримым.
        var padLength = (uint)target[cipherLength - 1];
        var good = ConstantTimeGreaterOrEqual((uint)cipherLength, padLength + MacLength + 1);
        var toCheck = Math.Min(MaxPaddingSpan, cipherLength);

        for (var index = 0; index < toCheck; index++)
        {
            var inPadding = ConstantTimeGreaterOrEqual(padLength, (uint)index);
            var actual = (uint)target[cipherLength - 1 - index];
            good &= ~(inPadding & (padLength ^ actual));
        }

        good = ConstantTimeEqual(good & 0xFF, 0xFF);

        // Негодное дополнение НЕ повод пропустить вычисление метки: пропуск целого хеширования —
        // это разница в микросекунды, то есть оракул Lucky13 в чистом виде. Вместо отказа
        // подставляем нулевую длину дополнения и считаем метку как ни в чём не бывало.
        padLength &= good;
        var contentLength = cipherLength - MacLength - (int)padLength - 1;

        // Для фреймворка включена проверка переполнения, а разбиение длины на два разряда — не
        // потеря данных, а формат поля. Без unchecked это бросало бы исключение на любой записи
        // длиннее 255 байт — то есть на первом же реальном ответе.
        unchecked
        {
            macHeader[MacHeaderLengthOffset] = (byte)(contentLength >> 8);
            macHeader[MacHeaderLengthOffset + 1] = (byte)contentLength;
        }

        Span<byte> expected = stackalloc byte[MacLength];
        ComputeMac(macHeader, target[..contentLength], expected);

        // Выравнивание счёта блоков сжатия: длина content зависит от секретного padding_length, а
        // с ней и время HMAC. Дохешируем недостающую длину вхолостую — с точностью до одного блока
        // это возвращает постоянство. Полную постоянность даёт только ручное управление блоками
        // (ssl3_cbc_digest_record); см. оговорку в описании класса.
        HashFiller(cipherLength - MacLength - 1 - contentLength);

        var macMatches = CryptographicOperations.FixedTimeEquals(expected, target.Slice(contentLength, MacLength));

        // ★ Побитовое «и», а не «&&», и это НЕ описка. Короткое замыкание пропустило бы сравнение
        // метки при негодном дополнении — то есть вернуло бы ровно ту разницу во времени, ради
        // устранения которой написан весь метод. Анализатор советует здесь «&&»; совет неверен.
#pragma warning disable S2178 // Short-circuit logic should be used in boolean contexts
        var ok = (good is not 0) & macMatches;
#pragma warning restore S2178

        plainLength = ok ? contentLength : 0;
        return ok;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(macKey);
        aes.Dispose();
    }

    /// <summary>
    /// MAC = HMAC-SHA1(mac_write_key, seq(8) || type(1) || version(2) || length(2) || content).
    /// </summary>
    /// <remarks>
    /// Тринадцать байт заголовка — ровно те же, что для наборов AEAD служат дополнительными
    /// аутентифицируемыми данными. Собираются они одним методом потока: две сборки одного
    /// заголовка неминуемо разошлись бы между отправкой и приёмом, а выглядело бы это как «сервер
    /// шлёт битые записи».
    /// </remarks>
    private void ComputeMac(ReadOnlySpan<byte> macHeader, ReadOnlySpan<byte> content, Span<byte> destination)
    {
        var total = macHeader.Length + content.Length;
        var buffer = ArrayPool<byte>.Shared.Rent(total);

        try
        {
            macHeader.CopyTo(buffer);
            content.CopyTo(buffer.AsSpan(macHeader.Length));
            HMACSHA1.HashData(macKey, buffer.AsSpan(0, total), destination);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

#pragma warning disable S4790 // Weak hashing algorithms should not be used
    /// <summary>
    /// Холостое хеширование на указанную длину — выравнивание времени, а не вычисление.
    /// </summary>
    /// <param name="length">Сколько байт «недосчитал» настоящий HMAC из-за длины дополнения.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void HashFiller(int length)
    {
        if ((uint)length > MaxPaddingSpan) length = MaxPaddingSpan;

        SHA1.HashData(FillerZeroes.AsSpan(0, length), fillerDigest);
    }
#pragma warning restore S4790 // Weak hashing algorithms should not be used

    /// <summary>Возвращает все единицы, если <paramref name="value"/> отрицательно как знаковое.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ConstantTimeMostSignificantBit(uint value) => unchecked((uint)((int)value >> 31));

    /// <summary>Возвращает все единицы, если <paramref name="left"/> меньше <paramref name="right"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ConstantTimeLess(uint left, uint right)
        => unchecked(ConstantTimeMostSignificantBit(left ^ ((left ^ right) | ((left - right) ^ right))));

    /// <summary>Возвращает все единицы, если <paramref name="left"/> не меньше <paramref name="right"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ConstantTimeGreaterOrEqual(uint left, uint right) => ~ConstantTimeLess(left, right);

    /// <summary>Возвращает все единицы, если аргументы равны.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ConstantTimeEqual(uint left, uint right)
    {
        var difference = left ^ right;
        return unchecked(ConstantTimeMostSignificantBit(~difference & (difference - 1)));
    }
}
