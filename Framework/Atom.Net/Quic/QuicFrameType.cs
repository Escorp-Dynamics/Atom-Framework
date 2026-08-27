namespace Atom.Net.Quic;

/// <summary>
/// Типы кадров QUIC (RFC 9000, §19).
/// </summary>
/// <remarks>
/// Тип кадра — переменной длины, но все определённые значения умещаются в один байт. Кадры
/// STREAM и ACK занимают диапазоны: младшие биты типа несут флаги, поэтому сравнивать надо не
/// на равенство, а по диапазону.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1027:Mark enums with FlagsAttribute", Justification = "Значения кадров — не флаги: STREAM и ACK занимают диапазоны, и побитовое объединение типов недопустимо.")]
public enum QuicFrameType : byte
{
    /// <summary>Заполнение до нужного размера пакета.</summary>
    Padding = 0x00,

    /// <summary>Проверка живости; обязывает получателя подтвердить пакет.</summary>
    Ping = 0x01,

    /// <summary>Подтверждение принятых пакетов.</summary>
    Ack = 0x02,

    /// <summary>Подтверждение со счётчиками ECN.</summary>
    AckWithEcn = 0x03,

    /// <summary>Отправитель прекращает передачу по потоку.</summary>
    ResetStream = 0x04,

    /// <summary>Получатель просит прекратить передачу по потоку.</summary>
    StopSending = 0x05,

    /// <summary>Данные рукопожатия TLS.</summary>
    Crypto = 0x06,

    /// <summary>Токен для последующих соединений.</summary>
    NewToken = 0x07,

    /// <summary>Начало диапазона кадров STREAM (0x08–0x0F).</summary>
    Stream = 0x08,

    /// <summary>Конец диапазона кадров STREAM.</summary>
    StreamMax = 0x0F,

    /// <summary>Новый предел данных соединения.</summary>
    MaxData = 0x10,

    /// <summary>Новый предел данных потока.</summary>
    MaxStreamData = 0x11,

    /// <summary>Новый предел числа двунаправленных потоков.</summary>
    MaxStreamsBidi = 0x12,

    /// <summary>Новый предел числа однонаправленных потоков.</summary>
    MaxStreamsUni = 0x13,

    /// <summary>Отправитель упёрся в предел данных соединения.</summary>
    DataBlocked = 0x14,

    /// <summary>Отправитель упёрся в предел данных потока.</summary>
    StreamDataBlocked = 0x15,

    /// <summary>Отправитель упёрся в предел числа двунаправленных потоков.</summary>
    StreamsBlockedBidi = 0x16,

    /// <summary>Отправитель упёрся в предел числа однонаправленных потоков.</summary>
    StreamsBlockedUni = 0x17,

    /// <summary>Новый идентификатор соединения.</summary>
    NewConnectionId = 0x18,

    /// <summary>Вывод идентификатора соединения из обращения.</summary>
    RetireConnectionId = 0x19,

    /// <summary>Проверка пути.</summary>
    PathChallenge = 0x1A,

    /// <summary>Ответ на проверку пути.</summary>
    PathResponse = 0x1B,

    /// <summary>Закрытие соединения по ошибке транспорта.</summary>
    ConnectionCloseTransport = 0x1C,

    /// <summary>Закрытие соединения по ошибке прикладного уровня.</summary>
    ConnectionCloseApplication = 0x1D,

    /// <summary>Рукопожатие подтверждено сервером.</summary>
    HandshakeDone = 0x1E,
}
