#pragma warning disable CA1008, CA1707

namespace Atom.Net.Tls;

/// <summary>
/// Шифронабор TLS (IANA codepoint). Это НЕ флаги — значения равны реальным кодам.
/// Обновляемый «справочник»: включает все TLS 1.3 и наиболее используемые TLS 1.2.
/// При необходимости добавляйте новые элементы — сериализация не привязана к фиксированному набору.
/// </summary>
public enum CipherSuite : ushort
{
    // TLS 1.3 (RFC 8446)

    /// <summary>
    /// 
    /// </summary>
    TLS_AES_128_GCM_SHA256 = 0x1301,
    /// <summary>
    /// 
    /// </summary>
    TLS_AES_256_GCM_SHA384 = 0x1302,
    /// <summary>
    /// 
    /// </summary>
    TLS_CHACHA20_POLY1305_SHA256 = 0x1303,
    /// <summary>
    /// 
    /// </summary>
    TLS_AES_128_CCM_SHA256 = 0x1304,
    /// <summary>
    /// 
    /// </summary>
    TLS_AES_128_CCM_8_SHA256 = 0x1305,

    // TLS 1.2 — ECDHE + AES-GCM

    /// <summary>
    /// 
    /// </summary>
    TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256 = 0xC02B,
    /// <summary>
    /// 
    /// </summary>
    TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384 = 0xC02C,
    /// <summary>
    /// 
    /// </summary>
    TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256 = 0xC02F,
    /// <summary>
    /// 
    /// </summary>
    TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384 = 0xC030,

    // TLS 1.2 — RSA + AES-GCM

    /// <summary>
    /// TLS 1.2: ECDHE_RSA_CHACHA20_POLY1305_SHA256 (IANA 0xCCA8).
    /// </summary>
    TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256 = 0xCCA8,
    /// <summary>
    /// TLS 1.2: ECDHE_ECDSA_CHACHA20_POLY1305_SHA256 (IANA 0xCCA9)
    /// </summary>
    TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256 = 0xCCA9,

    /// <summary>
    /// ECDHE_RSA с AES-128-CBC и SHA-1 (0xC013).
    /// </summary>
    /// <remarks>
    /// Наборы на CBC и обычном RSA современным соединением не выбираются, но браузеры продолжают
    /// их ПРЕДЛАГАТЬ ради совместимости со старыми серверами. Для мимикрии важно именно
    /// предложение: список шифров ClientHello — половина отпечатка JA3 и JA4.
    /// </remarks>
    TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA = 0xC013,

    /// <summary>ECDHE_RSA с AES-256-CBC и SHA-1 (0xC014).</summary>
    TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA = 0xC014,

    /// <summary>RSA с AES-128-GCM (0x009C).</summary>
    TLS_RSA_WITH_AES_128_GCM_SHA256 = 0x009C,

    /// <summary>RSA с AES-256-GCM (0x009D).</summary>
    TLS_RSA_WITH_AES_256_GCM_SHA384 = 0x009D,

    /// <summary>RSA с AES-128-CBC и SHA-1 (0x002F).</summary>
    TLS_RSA_WITH_AES_128_CBC_SHA = 0x002F,

    /// <summary>RSA с AES-256-CBC и SHA-1 (0x0035).</summary>
    TLS_RSA_WITH_AES_256_CBC_SHA = 0x0035,

    /// <summary>ECDHE_ECDSA с AES-128-CBC и SHA-1 (0xC009).</summary>
    TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA = 0xC009,

    /// <summary>ECDHE_ECDSA с AES-256-CBC и SHA-1 (0xC00A).</summary>
    TLS_ECDHE_ECDSA_WITH_AES_256_CBC_SHA = 0xC00A,

    /// <summary>
    /// ECDHE_ECDSA с 3DES (0xC008).
    /// </summary>
    /// <remarks>
    /// Древний набор, который никакой современный сервер не выберет. Существует здесь ровно
    /// затем, чтобы его ПРЕДЛАГАТЬ: Safari до сих пор перечисляет тройку 3DES в конце списка, и
    /// её отсутствие меняет ja3 так же, как отсутствие любого другого набора.
    /// </remarks>
    TLS_ECDHE_ECDSA_WITH_3DES_EDE_CBC_SHA = 0xC008,

    /// <summary>ECDHE_RSA с 3DES (0xC012).</summary>
    TLS_ECDHE_RSA_WITH_3DES_EDE_CBC_SHA = 0xC012,

    /// <summary>RSA с 3DES (0x000A).</summary>
    TLS_RSA_WITH_3DES_EDE_CBC_SHA = 0x000A,
}