#pragma warning disable CA1008

namespace Atom.Net.Tls;

/// <summary>
/// Идентификаторы групп, поддерживаемых в TLS (RFC 8446 §4.2.7, §8.1).
/// Используются в supported_groups и key_share.
/// </summary>
public enum NamedGroup : ushort
{
    // --- Elliptic Curves ---

    /// <summary>
    /// 
    /// </summary>
    Secp256r1 = 0x0017,
    /// <summary>
    /// 
    /// </summary>
    Secp384r1 = 0x0018,
    /// <summary>
    /// 
    /// </summary>
    Secp521r1 = 0x0019,
    /// <summary>
    /// 
    /// </summary>
    X25519 = 0x001D,
    /// <summary>
    /// 
    /// </summary>
    X448 = 0x001E,

    // --- Finite Field DH (optional, legacy) ---

    /// <summary>
    /// 
    /// </summary>
    Ffdhe2048 = 0x0100,
    /// <summary>
    /// 
    /// </summary>
    Ffdhe3072 = 0x0101,
    /// <summary>
    /// 
    /// </summary>
    Ffdhe4096 = 0x0102,
    /// <summary>
    /// 
    /// </summary>
    Ffdhe6144 = 0x0103,
    /// <summary>
    /// 
    /// </summary>
    Ffdhe8192 = 0x0104,
}