#pragma warning disable CA1008

namespace Atom.Net.Tls.Extensions;

/// <summary>
/// Алгоритмы сжатия сертификатов TLS (RFC 8879).
/// </summary>
public enum CertificateCompressionAlgorithm : ushort
{
    /// <summary>
    /// 
    /// </summary>
    Zlib = 0x0001,
    /// <summary>
    /// 
    /// </summary>
    Brotli = 0x0002,
    /// <summary>
    /// 
    /// </summary>
    Zstd = 0x0003,
}