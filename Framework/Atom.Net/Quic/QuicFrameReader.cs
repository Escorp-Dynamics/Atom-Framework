namespace Atom.Net.Quic;

/// <summary>
/// Последовательное чтение кадров из расшифрованной нагрузки пакета QUIC.
/// </summary>
/// <remarks>
/// Читатель — структура по ссылке и не выделяет памяти: нагрузка разбирается на месте, а тела
/// кадров отдаются срезами исходного буфера. На горячем пути соединения кадров проходят десятки
/// тысяч, и аллокация на каждый свела бы на нет смысл мультиплексирования.
///
/// Неизвестный тип кадра — это ошибка протокола, а не повод продолжить: длины кадров не
/// самоописывающиеся, и пропустить незнакомый кадр невозможно в принципе.
/// </remarks>
/// <param name="payload">Расшифрованная нагрузка пакета.</param>
public ref struct QuicFrameReader(ReadOnlySpan<byte> payload)
{
    private readonly ReadOnlySpan<byte> payload = payload;
    private int position;

    /// <summary>Остались ли непрочитанные кадры.</summary>
    public readonly bool HasMore => position < payload.Length;

    /// <summary>Тип последнего прочитанного кадра.</summary>
    public ulong FrameType { get; private set; }

    /// <summary>Идентификатор потока для кадров STREAM и связанных с потоками.</summary>
    public ulong StreamId { get; private set; }

    /// <summary>Смещение данных для кадров STREAM и CRYPTO.</summary>
    public ulong Offset { get; private set; }

    /// <summary>Данные кадров STREAM, CRYPTO и подобных.</summary>
    public ReadOnlySpan<byte> Data { get; private set; }

    /// <summary>Признак завершения потока для кадров STREAM.</summary>
    public bool IsFin { get; private set; }

    /// <summary>Наибольший подтверждённый номер пакета для кадров ACK.</summary>
    public ulong LargestAcknowledged { get; private set; }

    /// <summary>Диапазоны подтверждённых пакетов: пары «начало, конец» включительно.</summary>
    public IReadOnlyList<(ulong Start, ulong End)> AckRanges { get; private set; } = [];

    /// <summary>
    /// Буфер диапазонов, общий для всех кадров ACK в одном потоке разбора.
    /// </summary>
    /// <remarks>
    /// Разбор кадров идёт в ОДНОМ цикле чтения соединения, поэтому общий буфер на поток
    /// безопасен, а вызывающая сторона обязана прочитать диапазоны до следующего вызова
    /// <see cref="Read"/> — ровно так их и читают.
    /// </remarks>
    [ThreadStatic]
    private static List<(ulong Start, ulong End)>? ackRangeBuffer;

    /// <summary>Код ошибки для кадров закрытия соединения.</summary>
    public ulong ErrorCode { get; private set; }

    /// <summary>Значение предела для кадров MAX_DATA и MAX_STREAM_DATA.</summary>
    public ulong MaximumData { get; private set; }

    /// <summary>
    /// Читает следующий кадр.
    /// </summary>
    /// <returns><see langword="false"/>, если кадры кончились.</returns>
    public bool Read()
    {
        // Кадры PADDING идут подряд и по одному не интересны: пропускаем их пачкой.
        while (position < payload.Length && payload[position] is 0x00) position++;

        if (position >= payload.Length) return false;

        if (!QuicCodec.TryRead(payload[position..], out var type, out var read)) throw new InvalidOperationException("Оборванный тип кадра QUIC");
        position += read;

        FrameType = type;
        Data = [];
        IsFin = false;

        switch (type)
        {
            case (ulong)QuicFrameType.Ping:
            case (ulong)QuicFrameType.HandshakeDone:
                break;

            case (ulong)QuicFrameType.Ack:
            case (ulong)QuicFrameType.AckWithEcn:
                ReadAck(withEcn: type is (ulong)QuicFrameType.AckWithEcn);
                break;

            case (ulong)QuicFrameType.Crypto:
                Offset = ReadVarint();
                Data = ReadBytes((int)ReadVarint());
                break;

            case (ulong)QuicFrameType.NewToken:
                Data = ReadBytes((int)ReadVarint());
                break;

            case (ulong)QuicFrameType.ResetStream:
                StreamId = ReadVarint();
                ErrorCode = ReadVarint();
                _ = ReadVarint();
                break;

            case (ulong)QuicFrameType.StopSending:
                StreamId = ReadVarint();
                ErrorCode = ReadVarint();
                break;

            case (ulong)QuicFrameType.MaxData:
                MaximumData = ReadVarint();
                break;

            case (ulong)QuicFrameType.MaxStreamData:
                StreamId = ReadVarint();
                MaximumData = ReadVarint();
                break;

            case (ulong)QuicFrameType.MaxStreamsBidi:
            case (ulong)QuicFrameType.MaxStreamsUni:
                MaximumData = ReadVarint();
                break;

            case (ulong)QuicFrameType.DataBlocked:
            case (ulong)QuicFrameType.StreamsBlockedBidi:
            case (ulong)QuicFrameType.StreamsBlockedUni:
                _ = ReadVarint();
                break;

            case (ulong)QuicFrameType.StreamDataBlocked:
                StreamId = ReadVarint();
                _ = ReadVarint();
                break;

            case (ulong)QuicFrameType.NewConnectionId:
                ReadNewConnectionId();
                break;

            case (ulong)QuicFrameType.RetireConnectionId:
                _ = ReadVarint();
                break;

            case (ulong)QuicFrameType.PathChallenge:
            case (ulong)QuicFrameType.PathResponse:
                Data = ReadBytes(8);
                break;

            case (ulong)QuicFrameType.ConnectionCloseTransport:
                ErrorCode = ReadVarint();
                _ = ReadVarint();
                Data = ReadBytes((int)ReadVarint());
                break;

            case (ulong)QuicFrameType.ConnectionCloseApplication:
                ErrorCode = ReadVarint();
                Data = ReadBytes((int)ReadVarint());
                break;

            default:
                if (type is >= (ulong)QuicFrameType.Stream and <= (ulong)QuicFrameType.StreamMax) ReadStream(type);
                else throw new InvalidOperationException($"Неизвестный тип кадра QUIC 0x{type:X}");

                break;
        }

        return true;
    }

    /// <summary>
    /// Разбирает кадр STREAM: младшие биты типа задают наличие полей.
    /// </summary>
    private void ReadStream(ulong type)
    {
        StreamId = ReadVarint();
        Offset = (type & 0x04) is not 0 ? ReadVarint() : 0;

        // Бит длины: без него данные занимают ВЕСЬ остаток пакета. Такой кадр обязан быть
        // последним, и именно так экономят два-три байта на каждом пакете с данными.
        var length = (type & 0x02) is not 0 ? (int)ReadVarint() : payload.Length - position;

        Data = ReadBytes(length);
        IsFin = (type & 0x01) is not 0;
    }

    /// <summary>
    /// Выдаёт общий буфер диапазонов, очищая его от прошлого кадра.
    /// </summary>
    private static List<(ulong Start, ulong End)> RentRangeBuffer()
    {
        var buffer = ackRangeBuffer ??= new List<(ulong Start, ulong End)>(16);
        buffer.Clear();

        return buffer;
    }

    private void ReadNewConnectionId()
    {
        _ = ReadVarint();
        _ = ReadVarint();

        var length = payload[position++];
        Data = ReadBytes(length);

        // Токен сброса без состояния — ровно 16 байт, длиной не предваряется.
        position += 16;
    }

    private void ReadAck(bool withEcn)
    {
        LargestAcknowledged = ReadVarint();
        _ = ReadVarint();

        var rangeCount = ReadVarint();
        var firstRange = ReadVarint();

        // Список переиспользуется между кадрами: подтверждения приходят на КАЖДЫЙ пакет, и
        // создавать под них объект каждый раз — заметная доля мусора на горячем пути.
        var ranges = RentRangeBuffer();

        var largest = LargestAcknowledged;
        var smallest = largest - firstRange;
        ranges.Add((smallest, largest));

        for (var index = 0UL; index < rangeCount; index++)
        {
            var gap = ReadVarint();
            var range = ReadVarint();

            // Промежуток и диапазон закодированы «на единицу меньше»: соседние диапазоны не
            // могут соприкасаться, иначе они были бы одним диапазоном.
            largest = smallest - gap - 2;
            smallest = largest - range;
            ranges.Add((smallest, largest));
        }

        if (withEcn)
        {
            _ = ReadVarint();
            _ = ReadVarint();
            _ = ReadVarint();
        }

        AckRanges = ranges;
    }

    private ulong ReadVarint()
    {
        if (!QuicCodec.TryRead(payload[position..], out var value, out var read)) throw new InvalidOperationException("Оборванное число переменной длины");
        position += read;

        return value;
    }

    private ReadOnlySpan<byte> ReadBytes(int length)
    {
        if (length < 0 || position + length > payload.Length) throw new InvalidOperationException("Оборванный кадр QUIC");

        var slice = payload.Slice(position, length);
        position += length;

        return slice;
    }
}
