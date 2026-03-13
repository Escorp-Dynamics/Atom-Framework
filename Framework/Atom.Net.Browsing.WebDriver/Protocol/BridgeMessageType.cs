namespace Atom.Net.Browsing.WebDriver.Protocol;

/// <summary>
/// Тип сообщения протокола обмена между драйвером и расширением.
/// </summary>
public enum BridgeMessageType
{
    /// <summary>
    /// Запрос от драйвера к расширению.
    /// </summary>
    Request,

    /// <summary>
    /// Ответ расширения на запрос драйвера.
    /// </summary>
    Response,

    /// <summary>
    /// Событие, инициированное расширением.
    /// </summary>
    Event,

    /// <summary>
    /// Подтверждение соединения.
    /// </summary>
    Handshake,

    /// <summary>
    /// Сигнал проверки связи.
    /// </summary>
    Ping,

    /// <summary>
    /// Ответ на сигнал проверки связи.
    /// </summary>
    Pong,
}
