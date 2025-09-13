namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Перечисление режимов PSK-обмена, определённых в TLS 1.3 (RFC 8446, §4.2.9).
/// </summary>
public enum PskKeyExchangeMode : byte
{
    /// <summary>
    /// PSK-only key establishment (без DH) — менее безопасный, не рекомендуется.
    /// </summary>
    PskOnly = 0x00,
    /// <summary>
    /// PSK с DHE (диффи-хеллман) — основной и безопасный режим TLS 1.3.
    /// </summary>
    PskDheKe = 0x01,
}