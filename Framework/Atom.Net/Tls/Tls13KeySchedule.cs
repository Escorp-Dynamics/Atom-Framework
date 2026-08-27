using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Atom.Net.Tls;

/// <summary>
/// Расписание ключей TLS 1.3 (RFC 8446, §7.1).
/// </summary>
/// <remarks>
/// Выделено отдельным типом, а не спрятано внутри <see cref="Tls13Stream"/>, по двум причинам.
/// Во-первых, расписание — чистая функция от секретов и транскрипта, поэтому его можно проверять
/// модульными тестами без сети и без рукопожатия; для криптографии это обязательное условие, иначе
/// ошибка проявится не отказом, а молчаливой порчей трафика. Во-вторых, тот же вывод ключей нужен
/// возобновлению сессии и обновлению ключей — держать его в одном месте дешевле, чем повторять.
///
/// Реализация опирается на <see cref="HKDF"/> из платформы: собственная реализация HMAC здесь не
/// нужна и была бы лишним источником ошибок. Специфика TLS сводится к структуре метки
/// (<c>HkdfLabel</c>) и к порядку вычислений.
/// </remarks>
public static class Tls13KeySchedule
{
    /// <summary>Префикс меток TLS 1.3 (RFC 8446, §7.1).</summary>
    private const string LabelPrefix = "tls13 ";

    /// <summary>
    /// Выполняет HKDF-Expand-Label (RFC 8446, §7.1).
    /// </summary>
    /// <param name="hash">Хэш-функция, заданная набором шифров.</param>
    /// <param name="secret">Входной секрет.</param>
    /// <param name="label">Метка без префикса <c>tls13 </c>.</param>
    /// <param name="context">Контекст (обычно хэш транскрипта либо пустой).</param>
    /// <param name="length">Требуемая длина результата в байтах.</param>
    /// <returns>Выведенный ключевой материал.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] ExpandLabel(HashAlgorithmName hash, ReadOnlySpan<byte> secret, string label, ReadOnlySpan<byte> context, int length)
    {
        ArgumentException.ThrowIfNullOrEmpty(label);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        // HkdfLabel: uint16 length + opaque label<7..255> + opaque context<0..255>.
        // Длины полей — однобайтовые, поэтому и метка, и контекст ограничены 255 байтами.
        var fullLabel = LabelPrefix + label;
        var labelBytes = Encoding.ASCII.GetByteCount(fullLabel);
        // Проверяем явным броском, а не ThrowIf-хелпером: хелпер подставляет выражение вызывающей
        // стороны вместо имени параметра, и анализаторы требуют взаимоисключающего.
        if (labelBytes > 255) throw new ArgumentOutOfRangeException(nameof(label), "Метка HkdfLabel не помещается в один байт длины");
        if (context.Length > 255) throw new ArgumentOutOfRangeException(nameof(context), "Контекст HkdfLabel не помещается в один байт длины");

        var info = new byte[2 + 1 + labelBytes + 1 + context.Length];
        BinaryPrimitives.WriteUInt16BigEndian(info, (ushort)length);
        info[2] = (byte)labelBytes;
        Encoding.ASCII.GetBytes(fullLabel, info.AsSpan(3, labelBytes));
        info[3 + labelBytes] = (byte)context.Length;
        context.CopyTo(info.AsSpan(4 + labelBytes));

        var result = new byte[length];
        HKDF.Expand(hash, secret, result, info);
        return result;
    }

    /// <summary>
    /// Выполняет Derive-Secret (RFC 8446, §7.1): Expand-Label поверх хэша транскрипта.
    /// </summary>
    /// <param name="hash">Хэш-функция, заданная набором шифров.</param>
    /// <param name="secret">Входной секрет.</param>
    /// <param name="label">Метка без префикса.</param>
    /// <param name="transcriptHash">Хэш транскрипта рукопожатия на текущий момент.</param>
    /// <returns>Выведенный секрет длиной с хэш.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] DeriveSecret(HashAlgorithmName hash, ReadOnlySpan<byte> secret, string label, ReadOnlySpan<byte> transcriptHash)
        => ExpandLabel(hash, secret, label, transcriptHash, GetHashLength(hash));

    /// <summary>
    /// Вычисляет early secret — первую ступень расписания.
    /// </summary>
    /// <param name="hash">Хэш-функция.</param>
    /// <param name="preSharedKey">PSK; при отсутствии возобновления передаётся пустой спан.</param>
    /// <returns>Early secret.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] DeriveEarlySecret(HashAlgorithmName hash, ReadOnlySpan<byte> preSharedKey)
    {
        var length = GetHashLength(hash);

        // Без PSK на вход идёт нулевой ключ длиной с хэш — это предписано спецификацией, а не
        // упрощение: салт на первой ступени тоже нулевой.
        Span<byte> zero = stackalloc byte[length];
        var key = preSharedKey.IsEmpty ? zero : preSharedKey;

        var earlySecret = new byte[length];
        HKDF.Extract(hash, key, zero, earlySecret);
        return earlySecret;
    }

    /// <summary>
    /// Вычисляет handshake secret из early secret и общего секрета (EC)DHE.
    /// </summary>
    /// <param name="hash">Хэш-функция.</param>
    /// <param name="earlySecret">Early secret.</param>
    /// <param name="sharedSecret">Общий секрет обмена ключами.</param>
    /// <returns>Handshake secret.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] DeriveHandshakeSecret(HashAlgorithmName hash, ReadOnlySpan<byte> earlySecret, ReadOnlySpan<byte> sharedSecret)
    {
        // Между ступенями обязателен Derive-Secret с меткой "derived" по хэшу ПУСТОЙ строки:
        // пропуск этого шага даёт рабочий с виду, но несовместимый вывод ключей.
        var derived = DeriveSecret(hash, earlySecret, "derived", EmptyHash(hash));

        var handshakeSecret = new byte[GetHashLength(hash)];
        HKDF.Extract(hash, sharedSecret, derived, handshakeSecret);
        return handshakeSecret;
    }

    /// <summary>
    /// Вычисляет master secret из handshake secret.
    /// </summary>
    /// <param name="hash">Хэш-функция.</param>
    /// <param name="handshakeSecret">Handshake secret.</param>
    /// <returns>Master secret.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] DeriveMasterSecret(HashAlgorithmName hash, ReadOnlySpan<byte> handshakeSecret)
    {
        var derived = DeriveSecret(hash, handshakeSecret, "derived", EmptyHash(hash));
        var length = GetHashLength(hash);
        Span<byte> zero = stackalloc byte[length];

        var masterSecret = new byte[length];
        HKDF.Extract(hash, zero, derived, masterSecret);
        return masterSecret;
    }

    /// <summary>
    /// Выводит ключ и IV записи из трафикового секрета (RFC 8446, §7.3).
    /// </summary>
    /// <param name="hash">Хэш-функция.</param>
    /// <param name="trafficSecret">Трафиковый секрет направления.</param>
    /// <param name="keyLength">Длина ключа выбранного AEAD.</param>
    /// <param name="ivLength">Длина IV выбранного AEAD (для AES-GCM и ChaCha20 — 12).</param>
    /// <returns>Пара «ключ, IV».</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (byte[] Key, byte[] Iv) DeriveRecordKeys(HashAlgorithmName hash, ReadOnlySpan<byte> trafficSecret, int keyLength, int ivLength)
        => (ExpandLabel(hash, trafficSecret, "key", [], keyLength), ExpandLabel(hash, trafficSecret, "iv", [], ivLength));

    /// <summary>
    /// Выводит ключ для вычисления Finished (RFC 8446, §4.4.4).
    /// </summary>
    /// <param name="hash">Хэш-функция.</param>
    /// <param name="trafficSecret">Трафиковый секрет соответствующей стороны.</param>
    /// <returns>Ключ Finished.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] DeriveFinishedKey(HashAlgorithmName hash, ReadOnlySpan<byte> trafficSecret)
        => ExpandLabel(hash, trafficSecret, "finished", [], GetHashLength(hash));

    /// <summary>
    /// Вычисляет verify_data для сообщения Finished.
    /// </summary>
    /// <param name="hash">Хэш-функция.</param>
    /// <param name="finishedKey">Ключ, полученный <see cref="DeriveFinishedKey"/>.</param>
    /// <param name="transcriptHash">Хэш транскрипта на момент Finished.</param>
    /// <returns>Значение verify_data.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] ComputeVerifyData(HashAlgorithmName hash, ReadOnlySpan<byte> finishedKey, ReadOnlySpan<byte> transcriptHash)
        => hash == HashAlgorithmName.SHA384
            ? HMACSHA384.HashData(finishedKey, transcriptHash)
            : HMACSHA256.HashData(finishedKey, transcriptHash);

    /// <summary>
    /// Выводит следующий трафиковый секрет при обновлении ключей (RFC 8446, §7.2).
    /// </summary>
    /// <param name="hash">Хэш-функция.</param>
    /// <param name="trafficSecret">Текущий трафиковый секрет.</param>
    /// <returns>Секрет для последующих записей.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] DeriveNextTrafficSecret(HashAlgorithmName hash, ReadOnlySpan<byte> trafficSecret)
        => ExpandLabel(hash, trafficSecret, "traffic upd", [], GetHashLength(hash));

    /// <summary>
    /// Выводит resumption master secret (RFC 8446, §7.1).
    /// </summary>
    /// <param name="hash">Хэш-функция.</param>
    /// <param name="masterSecret">Master secret.</param>
    /// <param name="transcriptHash">Хэш транскрипта ПОСЛЕ Finished клиента.</param>
    /// <returns>Секрет, из которого выводятся PSK билетов сессии.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] DeriveResumptionMasterSecret(HashAlgorithmName hash, ReadOnlySpan<byte> masterSecret, ReadOnlySpan<byte> transcriptHash)
        => DeriveSecret(hash, masterSecret, "res master", transcriptHash);

    /// <summary>
    /// Выводит PSK конкретного билета сессии (RFC 8446, §4.6.1).
    /// </summary>
    /// <param name="hash">Хэш-функция согласованного набора.</param>
    /// <param name="resumptionMasterSecret">Секрет из <see cref="DeriveResumptionMasterSecret"/>.</param>
    /// <param name="ticketNonce">Значение ticket_nonce билета.</param>
    /// <returns>PSK для возобновления сессии.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] DeriveResumptionPsk(HashAlgorithmName hash, ReadOnlySpan<byte> resumptionMasterSecret, ReadOnlySpan<byte> ticketNonce)
        => ExpandLabel(hash, resumptionMasterSecret, "resumption", ticketNonce, GetHashLength(hash));

    /// <summary>
    /// Выводит binder key из early secret (RFC 8446, §4.2.11.2).
    /// </summary>
    /// <param name="hash">Хэш-функция набора, с которым выдавался билет.</param>
    /// <param name="earlySecret">Early secret, выведенный из PSK билета.</param>
    /// <param name="external">Внешний PSK («ext binder») либо билет сессии («res binder»).</param>
    /// <returns>Ключ для вычисления binder'а.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] DeriveBinderKey(HashAlgorithmName hash, ReadOnlySpan<byte> earlySecret, bool external)
        => DeriveSecret(hash, earlySecret, external ? "ext binder" : "res binder", EmptyHash(hash));

    /// <summary>
    /// Вычисляет значение binder'а — HMAC по хэшу усечённого ClientHello (RFC 8446, §4.2.11.2).
    /// </summary>
    /// <param name="hash">Хэш-функция набора, с которым выдавался билет.</param>
    /// <param name="binderKey">Ключ из <see cref="DeriveBinderKey"/>.</param>
    /// <param name="truncatedClientHelloHash">Хэш транскрипта с усечённым ClientHello.</param>
    /// <returns>Значение binder'а длиной с хэш.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] ComputePskBinder(HashAlgorithmName hash, ReadOnlySpan<byte> binderKey, ReadOnlySpan<byte> truncatedClientHelloHash)
        => hash == HashAlgorithmName.SHA384
            ? HMACSHA384.HashData(binderKey, truncatedClientHelloHash)
            : HMACSHA256.HashData(binderKey, truncatedClientHelloHash);

    /// <summary>
    /// Возвращает длину вывода хэш-функции в байтах.
    /// </summary>
    /// <param name="hash">Хэш-функция.</param>
    /// <returns>Длина в байтах.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetHashLength(HashAlgorithmName hash) => hash == HashAlgorithmName.SHA384 ? 48 : 32;

    /// <summary>
    /// Возвращает хэш пустой строки — он требуется на переходах между ступенями расписания.
    /// </summary>
    /// <param name="hash">Хэш-функция.</param>
    /// <returns>Хэш пустого входа.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] EmptyHash(HashAlgorithmName hash)
        => hash == HashAlgorithmName.SHA384 ? SHA384.HashData([]) : SHA256.HashData([]);
}
