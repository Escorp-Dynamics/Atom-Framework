namespace Atom.Net.Tls;

/// <summary>
/// Тип handshake-сообщений.
/// </summary>
public enum TlsHandshakeType : byte
{
    /// <summary>
    /// 
    /// </summary>
    HelloRequest = 0,   // legacy
    /// <summary>
    /// 
    /// </summary>
    ClientHello = 1,
    /// <summary>
    /// 
    /// </summary>
    ServerHello = 2,
    /// <summary>
    /// 
    /// </summary>
    HelloVerifyRequest = 3,   // DTLS
    /// <summary>
    /// 
    /// </summary>
    NewSessionTicket = 4,   // TLS 1.2; в TLS 1.3 — post-handshake тоже
    /// <summary>
    /// 
    /// </summary>
    EndOfEarlyData = 5,   // TLS 1.3
    /// <summary>
    /// 
    /// </summary>
    HelloRetryRequest = 6,   // TLS 1.3 (код логически используется в ServerHello-представлении)
    /// <summary>
    /// 
    /// </summary>
    EncryptedExtensions = 8,   // TLS 1.3
    /// <summary>
    /// 
    /// </summary>
    Certificate = 11,
    /// <summary>
    /// 
    /// </summary>
    ServerKeyExchange = 12,  // TLS 1.2
    /// <summary>
    /// 
    /// </summary>
    CertificateRequest = 13,
    /// <summary>
    /// 
    /// </summary>
    ServerHelloDone = 14,  // TLS 1.2
    /// <summary>
    /// 
    /// </summary>
    CertificateVerify = 15,
    /// <summary>
    /// 
    /// </summary>
    ClientKeyExchange = 16,  // TLS 1.2
    /// <summary>
    /// 
    /// </summary>
    Finished = 20,
    /// <summary>
    /// 
    /// </summary>
    KeyUpdate = 24,  // TLS 1.3, post-handshake
    /// <summary>
    /// 
    /// </summary>
    MessageHash = 254,  // TLS 1.3, для транскриптов при HRR
}