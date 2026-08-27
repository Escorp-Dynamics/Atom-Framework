namespace Atom.Net.Quic;

/// <summary>
/// Запись кадров QUIC в буфер пакета.
/// </summary>
/// <remarks>
/// Структура по ссылке поверх чужого буфера: пакет собирается на месте, без промежуточных
/// массивов. Каждый метод возвращает <see langword="false"/>, если кадр не поместился, — и это
/// штатная ситуация, а не ошибка: размер пакета ограничен путём, и вызывающая сторона обязана
/// уметь перенести кадр в следующий пакет.
/// </remarks>
/// <param name="buffer">Буфер пакета.</param>
public ref struct QuicFrameWriter(Span<byte> buffer)
{
    private readonly Span<byte> buffer = buffer;

    /// <summary>Сколько байт уже записано.</summary>
    public int Written { get; private set; }

    /// <summary>Сколько места осталось.</summary>
    public readonly int Remaining => buffer.Length - Written;

    /// <summary>
    /// Пишет кадр CRYPTO.
    /// </summary>
    /// <param name="offset">Смещение данных в потоке рукопожатия.</param>
    /// <param name="data">Данные.</param>
    /// <returns><see langword="true"/>, если кадр записан.</returns>
    public bool WriteCrypto(ulong offset, ReadOnlySpan<byte> data)
    {
        var size = 1 + QuicCodec.EncodedLength(offset) + QuicCodec.EncodedLength((ulong)data.Length) + data.Length;
        if (size > Remaining) return false;

        WriteVarint((ulong)QuicFrameType.Crypto);
        WriteVarint(offset);
        WriteVarint((ulong)data.Length);
        WriteBytes(data);

        return true;
    }

    /// <summary>
    /// Пишет кадр STREAM с явными смещением и длиной.
    /// </summary>
    /// <param name="streamId">Идентификатор потока.</param>
    /// <param name="offset">Смещение данных в потоке.</param>
    /// <param name="data">Данные.</param>
    /// <param name="fin">Закрывает ли кадр поток.</param>
    /// <returns><see langword="true"/>, если кадр записан.</returns>
    public bool WriteStream(ulong streamId, ulong offset, ReadOnlySpan<byte> data, bool fin)
    {
        // Биты типа: 0x04 — есть смещение, 0x02 — есть длина, 0x01 — конец потока.
        var type = (ulong)QuicFrameType.Stream | 0x02;
        if (offset > 0) type |= 0x04;
        if (fin) type |= 0x01;

        var size = QuicCodec.EncodedLength(type)
            + QuicCodec.EncodedLength(streamId)
            + (offset > 0 ? QuicCodec.EncodedLength(offset) : 0)
            + QuicCodec.EncodedLength((ulong)data.Length)
            + data.Length;

        if (size > Remaining) return false;

        WriteVarint(type);
        WriteVarint(streamId);
        if (offset > 0) WriteVarint(offset);
        WriteVarint((ulong)data.Length);
        WriteBytes(data);

        return true;
    }

    /// <summary>
    /// Пишет кадр ACK по накопленным диапазонам.
    /// </summary>
    /// <param name="ranges">Диапазоны принятых номеров, отсортированные по убыванию.</param>
    /// <param name="ackDelayMicroseconds">Задержка подтверждения в микросекундах.</param>
    /// <param name="ackDelayExponent">Показатель масштаба задержки из параметров транспорта.</param>
    /// <returns><see langword="true"/>, если кадр записан.</returns>
    /// <remarks>
    /// Диапазоны кодируются от наибольшего к наименьшему, промежутками между ними. Формат
    /// экономный, но требует строгой сортировки: перепутанный порядок сервер расценит как
    /// нарушение протокола и закроет соединение.
    /// </remarks>
    public bool WriteAck(scoped ReadOnlySpan<(ulong Start, ulong End)> ranges, ulong ackDelayMicroseconds, int ackDelayExponent)
    {
        if (ranges.Length is 0) return true;

        var start = Written;
        var delay = ackDelayMicroseconds >> ackDelayExponent;

        var largest = ranges[0].End;
        var firstRange = largest - ranges[0].Start;

        var required = 1
            + QuicCodec.EncodedLength(largest)
            + QuicCodec.EncodedLength(delay)
            + QuicCodec.EncodedLength((ulong)(ranges.Length - 1))
            + QuicCodec.EncodedLength(firstRange)
            + (ranges.Length * 8);

        if (required > Remaining) return false;

        WriteVarint((ulong)QuicFrameType.Ack);
        WriteVarint(largest);
        WriteVarint(delay);
        WriteVarint((ulong)(ranges.Length - 1));
        WriteVarint(firstRange);

        var smallest = ranges[0].Start;

        for (var index = 1; index < ranges.Length; index++)
        {
            var (rangeStart, rangeEnd) = ranges[index];

            WriteVarint(smallest - rangeEnd - 2);
            WriteVarint(rangeEnd - rangeStart);

            smallest = rangeStart;
        }

        return Written > start;
    }

    /// <summary>Пишет кадр PING.</summary>
    /// <returns><see langword="true"/>, если кадр записан.</returns>
    public bool WritePing() => WriteSingleByte((byte)QuicFrameType.Ping);

    /// <summary>
    /// Пишет кадр MAX_STREAM_DATA.
    /// </summary>
    /// <param name="streamId">Поток.</param>
    /// <param name="maximum">Новый предел.</param>
    /// <returns><see langword="true"/>, если кадр записан.</returns>
    public bool WriteMaxStreamData(ulong streamId, ulong maximum)
    {
        var size = 1 + QuicCodec.EncodedLength(streamId) + QuicCodec.EncodedLength(maximum);
        if (size > Remaining) return false;

        WriteVarint((ulong)QuicFrameType.MaxStreamData);
        WriteVarint(streamId);
        WriteVarint(maximum);

        return true;
    }

    /// <summary>
    /// Пишет кадр MAX_DATA.
    /// </summary>
    /// <param name="maximum">Новый предел данных соединения.</param>
    /// <returns><see langword="true"/>, если кадр записан.</returns>
    public bool WriteMaxData(ulong maximum)
    {
        var size = 1 + QuicCodec.EncodedLength(maximum);
        if (size > Remaining) return false;

        WriteVarint((ulong)QuicFrameType.MaxData);
        WriteVarint(maximum);

        return true;
    }

    /// <summary>
    /// Пишет кадр PATH_RESPONSE.
    /// </summary>
    /// <param name="data">Восемь байт из полученной проверки пути.</param>
    /// <returns><see langword="true"/>, если кадр записан.</returns>
    public bool WritePathResponse(ReadOnlySpan<byte> data)
    {
        if (1 + 8 > Remaining) return false;

        WriteVarint((ulong)QuicFrameType.PathResponse);
        WriteBytes(data[..8]);

        return true;
    }

    /// <summary>
    /// Пишет кадр закрытия соединения.
    /// </summary>
    /// <param name="errorCode">Код ошибки транспорта.</param>
    /// <param name="frameType">Тип кадра, вызвавшего ошибку, либо ноль.</param>
    /// <returns><see langword="true"/>, если кадр записан.</returns>
    public bool WriteConnectionClose(ulong errorCode, ulong frameType)
    {
        var size = 1 + QuicCodec.EncodedLength(errorCode) + QuicCodec.EncodedLength(frameType) + 1;
        if (size > Remaining) return false;

        WriteVarint((ulong)QuicFrameType.ConnectionCloseTransport);
        WriteVarint(errorCode);
        WriteVarint(frameType);
        WriteVarint(0);

        return true;
    }

    /// <summary>
    /// Дополняет пакет кадрами PADDING до указанной длины.
    /// </summary>
    /// <param name="totalLength">Требуемая длина полезной нагрузки.</param>
    /// <remarks>
    /// Пакет клиента с уровнем Initial обязан быть не короче 1200 байт: так проверяется, что путь
    /// пропускает достаточно крупные датаграммы. Без дополнения сервер имеет право не отвечать.
    /// </remarks>
    public void Pad(int totalLength)
    {
        while (Written < totalLength && Written < buffer.Length) buffer[Written++] = 0x00;
    }

    private bool WriteSingleByte(byte value)
    {
        if (Remaining < 1) return false;

        buffer[Written++] = value;
        return true;
    }

    private void WriteVarint(ulong value)
    {
        if (!QuicCodec.TryWrite(buffer[Written..], value, out var written)) throw new InvalidOperationException("Не хватило места для числа переменной длины");
        Written += written;
    }

    private void WriteBytes(ReadOnlySpan<byte> data)
    {
        data.CopyTo(buffer[Written..]);
        Written += data.Length;
    }
}
