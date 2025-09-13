#pragma warning disable CA1008

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Алгоритмы подписи TLS (SignatureScheme) по RFC 8446.
/// </summary>
public enum SignatureAlgorithm : ushort
{
    // --- RSA PKCS1 ---
    /// <summary>
    /// 
    /// </summary>
    RsaPkcs1Sha1 = 0x0201, // legacy
    /// <summary>
    /// 
    /// </summary>
    RsaPkcs1Sha256 = 0x0401,
    /// <summary>
    /// 
    /// </summary>
    RsaPkcs1Sha384 = 0x0501,
    /// <summary>
    /// 
    /// </summary>
    RsaPkcs1Sha512 = 0x0601,

    // --- ECDSA ---
    /// <summary>
    /// 
    /// </summary>
    EcdsaSha1 = 0x0203, // legacy
    /// <summary>
    /// 
    /// </summary>
    EcdsaSecp256r1Sha256 = 0x0403,
    /// <summary>
    /// 
    /// </summary>
    EcdsaSecp384r1Sha384 = 0x0503,
    /// <summary>
    /// 
    /// </summary>
    EcdsaSecp521r1Sha512 = 0x0603,

    // --- RSA PSS ---
    /// <summary>
    /// 
    /// </summary>
    RsaPssRsaeSha256 = 0x0804,
    /// <summary>
    /// 
    /// </summary>
    RsaPssRsaeSha384 = 0x0805,
    /// <summary>
    /// 
    /// </summary>
    RsaPssRsaeSha512 = 0x0806,
    /// <summary>
    /// 
    /// </summary>
    RsaPssPssSha256 = 0x0809,
    /// <summary>
    /// 
    /// </summary>
    RsaPssPssSha384 = 0x080A,
    /// <summary>
    /// 
    /// </summary>
    RsaPssPssSha512 = 0x080B,

    // --- EdDSA ---
    /// <summary>
    /// 
    /// </summary>
    Ed25519 = 0x0807,
    /// <summary>
    /// 
    /// </summary>
    Ed448 = 0x0808,
}