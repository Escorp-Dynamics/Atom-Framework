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
    /// <summary>
    /// Записи нет: партнёр закрыл соединение на её границе.
    /// </summary>
    /// <remarks>
    /// Не встречается на проводе — значение служебное. Отличает штатный конец потока от отказа:
    /// закрытие без предупреждения close_notify для ответа без объявленной длины означает именно
    /// конец тела.
    /// </remarks>
    None = 0,

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