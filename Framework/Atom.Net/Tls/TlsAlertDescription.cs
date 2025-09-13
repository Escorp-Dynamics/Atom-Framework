namespace Atom.Net.Tls;

/// <summary>
/// Коды тревог (не полный список, достаточно для начального уровня).
/// </summary>
public enum TlsAlertDescription : byte
{
    /// <summary>
    /// Соединение закрыто корректно.
    /// </summary>
    CloseNotify = 0,
    /// <summary>
    /// 
    /// </summary>
    UnexpectedMessage = 10,
    /// <summary>
    /// 
    /// </summary>
    BadRecordMac = 20,
    /// <summary>
    /// 
    /// </summary>
    DecryptionFailed = 21, // legacy
    /// <summary>
    /// 
    /// </summary>
    RecordOverflow = 22,
    /// <summary>
    /// 
    /// </summary>
    HandshakeFailure = 40,
    /// <summary>
    /// 
    /// </summary>
    BadCertificate = 42,
    /// <summary>
    /// 
    /// </summary>
    UnsupportedCertificate = 43,
    /// <summary>
    /// 
    /// </summary>
    CertificateRevoked = 44,
    /// <summary>
    /// 
    /// </summary>
    CertificateExpired = 45,
    /// <summary>
    /// 
    /// </summary>
    CertificateUnknown = 46,
    /// <summary>
    /// 
    /// </summary>
    IllegalParameter = 47,
    /// <summary>
    /// 
    /// </summary>
    UnknownCa = 48,
    /// <summary>
    /// 
    /// </summary>
    AccessDenied = 49,
    /// <summary>
    /// 
    /// </summary>
    DecodeError = 50,
    /// <summary>
    /// 
    /// </summary>
    DecryptError = 51,
    /// <summary>
    /// 
    /// </summary>
    ProtocolVersion = 70,
    /// <summary>
    /// 
    /// </summary>
    InsufficientSecurity = 71,
    /// <summary>
    /// 
    /// </summary>
    InternalError = 80,
    /// <summary>
    /// 
    /// </summary>
    InappropriateFallback = 86, // downgrade-sentinel (TLS_FALLBACK_SCSV контекст)
    /// <summary>
    /// 
    /// </summary>
    UserCanceled = 90,
    /// <summary>
    /// Клиент/сервер отклоняет renegotiation (TLS 1.2, RFC 5746).
    /// </summary>
    NoRenegotiation = 100,
    /// <summary>
    /// 
    /// </summary>
    MissingExtension = 109, // TLS 1.3
    /// <summary>
    /// 
    /// </summary>
    UnsupportedExtension = 110,
    /// <summary>
    /// 
    /// </summary>
    UnrecognizedName = 112,
    /// <summary>
    /// 
    /// </summary>
    BadCertificateStatusResponse = 113,
    /// <summary>
    /// 
    /// </summary>
    UnknownPskIdentity = 115,
    /// <summary>
    /// 
    /// </summary>
    CertificateRequired = 116,
    /// <summary>
    /// ALPN: нет согласованного протокола приложений.
    /// </summary>
    NoApplicationProtocol = 120,
}