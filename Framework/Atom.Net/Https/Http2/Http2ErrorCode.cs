namespace Atom.Net.Https.Http2;

/// <summary>
/// Коды ошибок HTTP/2 (RFC 9113, §7).
/// </summary>
/// <remarks>
/// Едут в кадрах RST_STREAM и GOAWAY и объясняют причину отказа. Различать их стоит не ради
/// формальности: <see cref="RefusedStream"/> означает «повтори», <see cref="Cancel"/> — «мне это
/// больше не нужно», а <see cref="EnhanceYourCalm"/> — «сбавь темп», и путать их значит либо
/// молотиться в закрытую дверь, либо бросить запрос, который повторился бы успешно.
/// </remarks>
public enum Http2ErrorCode : uint
{
    /// <summary>Штатное завершение без ошибки.</summary>
    NoError = 0x00,

    /// <summary>Нарушение протокола.</summary>
    ProtocolError = 0x01,

    /// <summary>Внутренняя ошибка получателя.</summary>
    InternalError = 0x02,

    /// <summary>Нарушение правил управления потоком.</summary>
    FlowControlError = 0x03,

    /// <summary>Подтверждение SETTINGS не пришло вовремя.</summary>
    SettingsTimeout = 0x04,

    /// <summary>Кадр получен для закрытого потока.</summary>
    StreamClosed = 0x05,

    /// <summary>Недопустимый размер кадра.</summary>
    FrameSizeError = 0x06,

    /// <summary>Поток не был обработан; запрос можно безопасно повторить.</summary>
    RefusedStream = 0x07,

    /// <summary>Поток больше не нужен отправителю.</summary>
    Cancel = 0x08,

    /// <summary>Ошибка состояния сжатия заголовков; соединение дальше непригодно.</summary>
    CompressionError = 0x09,

    /// <summary>Ошибка соединения, установленного методом CONNECT.</summary>
    ConnectError = 0x0A,

    /// <summary>Слишком высокий темп запросов.</summary>
    EnhanceYourCalm = 0x0B,

    /// <summary>Параметры защиты транспорта не отвечают требованиям.</summary>
    InadequateSecurity = 0x0C,

    /// <summary>Требуется HTTP/1.1.</summary>
    Http11Required = 0x0D,
}
