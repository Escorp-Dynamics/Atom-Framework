using Atom.Net.Quic;

namespace Atom.Net.Https.Http3;

/// <summary>
/// Типы кадров HTTP/3 (RFC 9114, §7.2).
/// </summary>
/// <remarks>
/// Кадров заметно меньше, чем в HTTP/2, и это не упрощение, а следствие разделения слоёв:
/// мультиплексирование, управление потоком и приоритеты забрал себе QUIC. HTTP/3 остаётся
/// разметкой поверх готовых потоков.
/// </remarks>
public enum Http3FrameType : ulong
{
    /// <summary>Тело сообщения.</summary>
    Data = 0x00,

    /// <summary>Блок заголовков, сжатый QPACK.</summary>
    Headers = 0x01,

    /// <summary>Параметры соединения; едут по управляющему потоку.</summary>
    Settings = 0x04,

    /// <summary>Отправитель прекращает принимать новые запросы.</summary>
    GoAway = 0x07,

    /// <summary>Предел идентификаторов серверных отправок.</summary>
    MaxPushId = 0x0D,
}

/// <summary>
/// Идентификаторы параметров HTTP/3.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "Нулевого идентификатора параметра в HTTP/3 нет: значение 0x00 зарезервировано и не используется.")]
public enum Http3SettingId : ulong
{
    /// <summary>Предельный размер таблицы QPACK.</summary>
    QpackMaxTableCapacity = 0x01,

    /// <summary>Предельный размер списка заголовков.</summary>
    MaxFieldSectionSize = 0x06,

    /// <summary>Сколько блоков заголовков допускается держать заблокированными.</summary>
    QpackBlockedStreams = 0x07,

    /// <summary>Поддержка расширенного CONNECT.</summary>
    EnableConnectProtocol = 0x08,

    /// <summary>Поддержка датаграмм HTTP (RFC 9297); Chrome объявляет её всегда.</summary>
    H3Datagram = 0x33,

    /// <summary>
    /// Поддержка WebTransport.
    /// </summary>
    /// <remarks>
    /// Значение из чернового описания WebTransport поверх HTTP/3 и стандартным не является.
    /// Отправляет его Firefox: в его стеке параметр входит в набор безусловно, и по одному этому
    /// числу браузер отличим от движков Chromium, которые его не шлют.
    /// </remarks>
    EnableWebTransport = 0x2B60_3742,

    /// <summary>
    /// Датаграммы HTTP/3 по черновику 04.
    /// </summary>
    /// <remarks>
    /// Firefox отправляет ОБА номера подряд — сначала черновой, затем окончательный 0x33.
    /// Выглядит избыточно, но именно эта пара и отличает его на проводе.
    /// </remarks>
    H3DatagramDraft04 = 0x00FF_D277,
}

/// <summary>
/// Чтение и запись кадров HTTP/3.
/// </summary>
/// <remarks>
/// Кадр — это тип и длина, оба числами переменной длины QUIC, затем тело. Формат намеренно
/// повторяет кодирование транспорта: отдельного целочисленного представления у HTTP/3 нет.
/// </remarks>
public static class Http3Frame
{
    /// <summary>
    /// Записывает заголовок кадра.
    /// </summary>
    /// <param name="destination">Буфер.</param>
    /// <param name="type">Тип кадра.</param>
    /// <param name="length">Длина тела.</param>
    /// <returns>Число записанных байт.</returns>
    public static int WriteHeader(Span<byte> destination, Http3FrameType type, int length)
    {
        var offset = 0;

        if (!QuicCodec.TryWrite(destination, (ulong)type, out var written)) throw new InvalidOperationException("Не хватило места для типа кадра HTTP/3");
        offset += written;

        if (!QuicCodec.TryWrite(destination[offset..], (ulong)length, out written)) throw new InvalidOperationException("Не хватило места для длины кадра HTTP/3");
        offset += written;

        return offset;
    }

    /// <summary>
    /// Возвращает длину заголовка кадра.
    /// </summary>
    /// <param name="type">Тип кадра.</param>
    /// <param name="length">Длина тела.</param>
    /// <returns>Число байт заголовка.</returns>
    public static int GetHeaderLength(Http3FrameType type, int length)
        => QuicCodec.EncodedLength((ulong)type) + QuicCodec.EncodedLength((ulong)length);

    /// <summary>
    /// Пытается прочитать заголовок кадра.
    /// </summary>
    /// <param name="source">Данные потока.</param>
    /// <param name="type">Тип кадра.</param>
    /// <param name="length">Длина тела.</param>
    /// <param name="headerLength">Сколько байт занял заголовок.</param>
    /// <returns><see langword="true"/>, если заголовок прочитан целиком.</returns>
    public static bool TryReadHeader(ReadOnlySpan<byte> source, out Http3FrameType type, out ulong length, out int headerLength)
    {
        type = default;
        length = 0;
        headerLength = 0;

        if (!QuicCodec.TryRead(source, out var rawType, out var typeLength)) return false;
        if (!QuicCodec.TryRead(source[typeLength..], out length, out var lengthLength)) return false;

        type = (Http3FrameType)rawType;
        headerLength = typeLength + lengthLength;

        return true;
    }

    /// <summary>
    /// Записывает кадр SETTINGS с параметрами профиля.
    /// </summary>
    /// <param name="destination">Буфер.</param>
    /// <param name="settings">Параметры в порядке отправки.</param>
    /// <returns>Число записанных байт.</returns>
    /// <remarks>
    /// Состав и ПОРЯДОК параметров наблюдаемы сервером и различаются между браузерами — ровно
    /// как SETTINGS в HTTP/2. Поэтому порядок задаётся вызывающей стороной, а не сортируется.
    /// </remarks>
    public static int WriteSettings(Span<byte> destination, IReadOnlyList<(Http3SettingId Id, ulong Value)> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Span<byte> body = stackalloc byte[128];
        var bodyLength = 0;

        foreach (var (id, value) in settings)
        {
            if (!QuicCodec.TryWrite(body[bodyLength..], (ulong)id, out var written)) throw new InvalidOperationException("Не хватило места для параметра HTTP/3");
            bodyLength += written;

            if (!QuicCodec.TryWrite(body[bodyLength..], value, out written)) throw new InvalidOperationException("Не хватило места для значения параметра HTTP/3");
            bodyLength += written;
        }

        var offset = WriteHeader(destination, Http3FrameType.Settings, bodyLength);
        body[..bodyLength].CopyTo(destination[offset..]);

        return offset + bodyLength;
    }

    /// <summary>
    /// Записывает подставной кадр с зарезервированным типом.
    /// </summary>
    /// <param name="destination">Буфер.</param>
    /// <returns>Число записанных байт.</returns>
    /// <remarks>
    /// Тип берётся из ряда, который спецификация (RFC 9114, §7.2.8) объявляет зарезервированным
    /// именно под это: получатель ОБЯЗАН такой кадр пропустить. Смысл тот же, что у GREASE в
    /// TLS, — не давать посредникам и серверам полагаться на закрытый набор типов.
    ///
    /// Firefox отправляет его сразу вслед за параметрами по управляющему потоку, и отсутствие
    /// кадра там, где браузер его шлёт, наблюдаемо ровно так же, как лишний параметр.
    /// </remarks>
    public static int WriteGrease(Span<byte> destination)
    {
        Span<byte> random = stackalloc byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(random);

        // Ряд зарезервированных типов: 0x1F * N + 0x21. Делитель ограничивает N так, чтобы тип
        // укладывался в два байта переменной длины — как это и выглядит у браузера.
        var type = (System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(random) % 0x0F * 0x1F) + 0x21;

        var offset = 0;

        if (!QuicCodec.TryWrite(destination, type, out var written)) throw new InvalidOperationException("Не хватило места для подставного кадра HTTP/3");
        offset += written;

        if (!QuicCodec.TryWrite(destination[offset..], 0, out written)) throw new InvalidOperationException("Не хватило места для длины подставного кадра");

        return offset + written;
    }
}
