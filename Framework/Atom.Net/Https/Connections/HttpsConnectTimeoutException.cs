namespace Atom.Net.Https.Connections;

/// <summary>
/// Соединение не удалось установить за отведённое время.
/// </summary>
/// <remarks>
/// ★ Отдельный тип нужен, чтобы отличить «не дозвонились» от «не дождались ответа». Разница
/// решающая: при неудачном ПОДКЛЮЧЕНИИ запрос не отправлялся вовсе, и повторить его можно без
/// всякого риска повторного действия. Таймаут же на ответе означает обратное — запрос мог быть
/// принят и обработан, и повторять его нельзя.
///
/// Различать это по тексту сообщения нельзя: тексты меняются, а решение о повторе от смысла
/// отказа зависит напрямую.
/// </remarks>
public sealed class HttpsConnectTimeoutException : TimeoutException
{
    /// <summary>Создаёт исключение.</summary>
    public HttpsConnectTimeoutException() { }

    /// <summary>Создаёт исключение с описанием.</summary>
    /// <param name="message">Описание.</param>
    public HttpsConnectTimeoutException(string message) : base(message) { }

    /// <summary>Создаёт исключение с описанием и причиной.</summary>
    /// <param name="message">Описание.</param>
    /// <param name="innerException">Причина.</param>
    public HttpsConnectTimeoutException(string message, Exception innerException) : base(message, innerException) { }
}
