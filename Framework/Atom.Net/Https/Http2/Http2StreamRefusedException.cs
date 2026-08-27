namespace Atom.Net.Https.Http2;

/// <summary>
/// Сервер отказал в потоке, не начав обрабатывать запрос.
/// </summary>
/// <remarks>
/// Отдельный тип нужен ровно для одного: отличить «повтори» от «отвергнуто». Код
/// REFUSED_STREAM (RFC 9113, §8.7) означает, что запрос НЕ ДОШЁЛ до обработки — сервер упёрся в
/// предел потоков, начал закрывать соединение или разгружается. Такой запрос можно повторить на
/// другом соединении без всякого риска повторного действия, и браузеры так и делают.
///
/// Отличать это по тексту сообщения было бы нельзя: тексты меняются, а поведение при повторе
/// зависит от точного смысла отказа.
/// </remarks>
public sealed class Http2StreamRefusedException : IOException
{
    /// <summary>Создаёт исключение.</summary>
    public Http2StreamRefusedException() { }

    /// <summary>Создаёт исключение с описанием.</summary>
    /// <param name="message">Описание.</param>
    public Http2StreamRefusedException(string message) : base(message) { }

    /// <summary>Создаёт исключение с описанием и причиной.</summary>
    /// <param name="message">Описание.</param>
    /// <param name="innerException">Причина.</param>
    public Http2StreamRefusedException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Создаёт исключение с описанием и кодом ошибки.</summary>
    /// <param name="message">Описание.</param>
    /// <param name="hresult">Код ошибки.</param>
    public Http2StreamRefusedException(string message, int hresult) : base(message, hresult) { }
}
