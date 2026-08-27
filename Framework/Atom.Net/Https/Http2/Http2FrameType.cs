namespace Atom.Net.Https.Http2;

/// <summary>
/// Тип кадра HTTP/2 (RFC 9113, §6).
/// </summary>
public enum Http2FrameType : byte
{
    /// <summary>Полезные данные потока.</summary>
    Data = 0x00,

    /// <summary>Блок заголовков, открывающий поток.</summary>
    Headers = 0x01,

    /// <summary>Приоритет потока. Устарел в RFC 9113, но браузеры его шлют.</summary>
    Priority = 0x02,

    /// <summary>Досрочное закрытие потока с указанием причины.</summary>
    ResetStream = 0x03,

    /// <summary>Параметры соединения.</summary>
    Settings = 0x04,

    /// <summary>Обещание server push.</summary>
    PushPromise = 0x05,

    /// <summary>Проверка живости и измерение задержки.</summary>
    Ping = 0x06,

    /// <summary>Завершение соединения.</summary>
    GoAway = 0x07,

    /// <summary>Приращение окна управления потоком.</summary>
    WindowUpdate = 0x08,

    /// <summary>Продолжение блока заголовков, не поместившегося в один кадр.</summary>
    Continuation = 0x09,
}
