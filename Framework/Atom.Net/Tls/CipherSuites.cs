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
}