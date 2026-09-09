using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Atom.Net.Tls;

/// <summary>
/// Способ согласования premaster secret в TLS 1.2.
/// </summary>
internal enum Tls12KeyExchange
{
    /// <summary>
    /// Эфемерный обмен на эллиптических кривых: сервер присылает ServerKeyExchange со своей точкой.
    /// </summary>
    Ecdhe,

    /// <summary>
    /// Классический обмен на RSA: ServerKeyExchange не приходит вовсе, premaster придумывает клиент
    /// и шифрует его открытым ключом из сертификата сервера (RFC 5246 §7.4.7.1).
    /// </summary>
    Rsa,
}

/// <summary>
/// Способ защиты записи.
/// </summary>
internal enum Tls12BulkCipher
{
    /// <summary>AES в режиме GCM: AEAD, в записи едет восьмибайтовый явный вектор.</summary>
    AesGcm,

    /// <summary>ChaCha20-Poly1305: AEAD, явного вектора в записи нет (RFC 7905).</summary>
    ChaCha20Poly1305,

    /// <summary>AES в режиме CBC с отдельным HMAC: MAC-then-encrypt, явный вектор 16 байт (RFC 5246 §6.2.3.2).</summary>
    AesCbc,
}

/// <summary>
/// Параметры набора шифров TLS 1.2 — единственный источник правды о его свойствах.
/// </summary>
/// <remarks>
/// ★ Зачем таблица, а не набор проверок по месту. Свойства набора нужны минимум в шести местах:
/// длина ключа и вектора при нарезке key_block, хэш PRF, форма записи, выбор примитива, допуск
/// набора в ServerHello, наличие MAC-ключей. Пока каждое место перечисляло коды само, добавление
/// набора означало шесть согласованных правок, а любая забытая давала МОЛЧАЛИВУЮ поломку в конце
/// внешне безупречного рукопожатия.
///
/// Два примера, за которые уже заплачено разбирательством:
/// • <c lang="text">0x009D</c> (RSA + AES-256-GCM) требует PRF на SHA-384. Проверка вида
///   «<c lang="text">suite is 0xC030 or 0xC02C</c>» его не знает и вернула бы SHA-256 — master secret вышел бы
///   неверным, а сервер отверг бы Finished. Выглядит это как «сервер сломался».
/// • тот же <c lang="text">0x009D</c> требует 32-байтовый ключ; перечисление «<c lang="text">0xC030 or 0xC02C or 0xCCA8…</c>»
///   выдало бы 16. Симптом ровно тот же, причина другая — различить их по проводу невозможно.
///
/// Поэтому здесь перечислены ВСЕ поля разом: добавить набор — значит дописать одну строку, а не
/// вспомнить шесть мест.
/// </remarks>
internal readonly record struct Tls12CipherSuiteParameters
{
    /// <summary>Длина блока AES — она же длина явного вектора инициализации в записи CBC.</summary>
    internal const int AesBlockLength = 16;

    /// <summary>Длина метки HMAC-SHA1 и, одновременно, длина MAC-ключа для наборов «…_SHA».</summary>
    internal const int Sha1MacLength = 20;

    /// <summary>Способ получения premaster secret.</summary>
    public Tls12KeyExchange KeyExchange { get; init; }

    /// <summary>Способ защиты записи.</summary>
    public Tls12BulkCipher Bulk { get; init; }

    /// <summary>Длина ключа шифрования, байт.</summary>
    public int EncKeyLength { get; init; }

    /// <summary>Длина MAC-ключа, байт. Ноль для AEAD: там подлинность обеспечивает сам примитив.</summary>
    public int MacKeyLength { get; init; }

    /// <summary>
    /// Длина фиксированной части вектора инициализации из key_block, байт.
    /// </summary>
    /// <remarks>
    /// У AES-GCM это четырёхбайтовая соль, у ChaCha20 — весь двенадцатибайтовый IV, у CBC в
    /// TLS 1.2 — НОЛЬ: вектор целиком случаен на каждую запись и едет в ней явно. Именно ради
    /// этого явный вектор в 1.1/1.2 и вводили — предсказуемый IV режима CBC из TLS 1.0 и есть
    /// уязвимость BEAST (CVE-2011-3389).
    /// </remarks>
    public int FixedIvLength { get; init; }

    /// <summary>Длина явного вектора инициализации, едущего в начале записи, байт.</summary>
    public int RecordIvLength { get; init; }

    /// <summary>Длина тега подлинности (AEAD) либо метки HMAC (CBC), байт.</summary>
    public int TagLength { get; init; }

    /// <summary>Хэш, на котором построен PRF этого набора (RFC 5246 §5).</summary>
    public HashAlgorithmName PrfHash { get; init; }

    /// <summary>
    /// Доступен ли набор в этой сборке рантайма.
    /// </summary>
    /// <remarks>
    /// Отдельно от наличия в таблице: ChaCha20-Poly1305 может отсутствовать в криптобиблиотеке
    /// платформы, и объявить такой набор поддержанным раньше времени — хуже, чем отказать: вместо
    /// внятного отказа получится провал расшифровки посреди обмена.
    /// </remarks>
    public bool IsAvailable => Bulk is not Tls12BulkCipher.ChaCha20Poly1305 || ChaCha20Poly1305Aead.IsSupported;

    /// <summary>
    /// Возвращает параметры набора, если он реализован.
    /// </summary>
    /// <param name="suite">Код набора (IANA).</param>
    /// <param name="parameters">Параметры набора.</param>
    /// <returns><see langword="true"/>, если набор есть в таблице реализованных.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGet(ushort suite, out Tls12CipherSuiteParameters parameters)
    {
        switch (suite)
        {
            // ECDHE + AES-GCM
            case 0xC02B: parameters = Gcm(Tls12KeyExchange.Ecdhe, 16, HashAlgorithmName.SHA256); return true;
            case 0xC02C: parameters = Gcm(Tls12KeyExchange.Ecdhe, 32, HashAlgorithmName.SHA384); return true;
            case 0xC02F: parameters = Gcm(Tls12KeyExchange.Ecdhe, 16, HashAlgorithmName.SHA256); return true;
            case 0xC030: parameters = Gcm(Tls12KeyExchange.Ecdhe, 32, HashAlgorithmName.SHA384); return true;

            // ECDHE + ChaCha20-Poly1305 (RFC 7905). PRF у обоих — SHA-256.
            case 0xCCA8:
            case 0xCCA9:
                parameters = new()
                {
                    KeyExchange = Tls12KeyExchange.Ecdhe,
                    Bulk = Tls12BulkCipher.ChaCha20Poly1305,
                    EncKeyLength = 32,
                    MacKeyLength = 0,
                    FixedIvLength = 12,
                    RecordIvLength = 0,
                    TagLength = 16,
                    PrfHash = HashAlgorithmName.SHA256,
                };
                return true;

            // RSA + AES-GCM. ★ У 0x009D PRF именно SHA-384 и ключ именно 32 байта.
            case 0x009C: parameters = Gcm(Tls12KeyExchange.Rsa, 16, HashAlgorithmName.SHA256); return true;
            case 0x009D: parameters = Gcm(Tls12KeyExchange.Rsa, 32, HashAlgorithmName.SHA384); return true;

            // CBC + HMAC-SHA1. PRF у всех наборов «…_CBC_SHA» — SHA-256: суффикс SHA относится к
            // MAC записи, а не к PRF, и путать их значит получить неверный master secret.
            case 0x002F: parameters = Cbc(Tls12KeyExchange.Rsa, 16); return true;
            case 0x0035: parameters = Cbc(Tls12KeyExchange.Rsa, 32); return true;
            case 0xC013: parameters = Cbc(Tls12KeyExchange.Ecdhe, 16); return true;
            case 0xC014: parameters = Cbc(Tls12KeyExchange.Ecdhe, 32); return true;
            case 0xC009: parameters = Cbc(Tls12KeyExchange.Ecdhe, 16); return true;
            case 0xC00A: parameters = Cbc(Tls12KeyExchange.Ecdhe, 32); return true;

            default:
                parameters = default;
                return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Tls12CipherSuiteParameters Gcm(Tls12KeyExchange kex, int keyLength, HashAlgorithmName prf) => new()
    {
        KeyExchange = kex,
        Bulk = Tls12BulkCipher.AesGcm,
        EncKeyLength = keyLength,
        MacKeyLength = 0,
        FixedIvLength = 4,
        RecordIvLength = 8,
        TagLength = 16,
        PrfHash = prf,
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Tls12CipherSuiteParameters Cbc(Tls12KeyExchange kex, int keyLength) => new()
    {
        KeyExchange = kex,
        Bulk = Tls12BulkCipher.AesCbc,
        EncKeyLength = keyLength,
        MacKeyLength = Sha1MacLength,
        FixedIvLength = 0,
        RecordIvLength = AesBlockLength,
        TagLength = Sha1MacLength,
        PrfHash = HashAlgorithmName.SHA256,
    };
}
