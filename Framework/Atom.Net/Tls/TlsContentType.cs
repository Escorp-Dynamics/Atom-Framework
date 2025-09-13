#pragma warning disable CA1008

namespace Atom.Net.Tls;

/// <summary>
/// Тип содержимого TLS-записи (record layer).
/// </summary>
public enum TlsContentType : byte
{
    /// <summary>
    /// 
    /// </summary>
    ChangeCipherSpec = 20,
    /// <summary>
    /// 
    /// </summary>
    Alert = 21,
    /// <summary>
    /// 
    /// </summary>
    Handshake = 22,
    /// <summary>
    /// 
    /// </summary>
    ApplicationData = 23,
    /// <summary>
    /// 
    /// </summary>
    Heartbeat = 24, // не используем, но оставлено для полноты.
}