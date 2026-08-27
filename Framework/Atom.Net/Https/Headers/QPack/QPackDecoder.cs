using System.Runtime.CompilerServices;
using Atom.Net.Https.Headers.HPack;
using Atom.Net.Https.Headers.QPack;

namespace Atom.Net.Https.Headers;

/// <summary>
/// Представляет декодировщик QPACK.
/// </summary>
public sealed class QPackDecoder : IHeadersDecoder
{
    IEnumerable<KeyValuePair<string, string>> IHeadersDecoder.Decode(ReadOnlySpan<byte> block)
    {
        // Копируем в Memory для избежания захвата Span через yield/await
        var mem = new byte[block.Length];
        block.CopyTo(mem);
        return Decode(mem);
    }
    private readonly QPackDynamicTable dynamicTable;

    /// <summary>
    /// Размер динамической таблицы.
    /// </summary>
    public int DynamicTableSize { get; private set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="QPackDecoder"/>.
    /// </summary>
    /// <param name="dynamicTableSize">Размер динамической таблицы.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public QPackDecoder(int dynamicTableSize = 4096)
    {
        if (dynamicTableSize < 0) dynamicTableSize = 0;

        DynamicTableSize = dynamicTableSize;
        dynamicTable = new QPackDynamicTable(dynamicTableSize);
        encoderInstructions = new QPackDecoderStream(dynamicTable);
    }

    private readonly QPackDecoderStream encoderInstructions;

    /// <summary>
    /// Применяет инструкции встречного потока кодировщика к динамической таблице.
    /// </summary>
    /// <param name="instructions">Данные однонаправленного потока типа 0x02.</param>
    /// <returns>Сколько байт разобрано полностью; хвост надо сохранить до следующего куска.</returns>
    /// <remarks>
    /// Без этого динамическая таблица остаётся пустой, а сервер ссылается на её записи — и разбор
    /// заголовков падает на «запись уже вытеснена». Именно поэтому объявлять ненулевую ёмкость
    /// таблицы можно ТОЛЬКО вместе с обработкой этого потока: одно без другого гарантированно
    /// ломает ответы.
    /// </remarks>
    public int ApplyEncoderInstructions(ReadOnlySpan<byte> instructions)
    {
        var consumed = encoderInstructions.Apply(instructions);

        // Будим тех, кто ждёт вставок: блок заголовков мог ссылаться ровно на эти записи.
        Interlocked.Exchange(ref insertions, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

        return consumed;
    }

    private TaskCompletionSource insertions = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Сколько вставок в динамическую таблицу принято.</summary>
    public int InsertCount => dynamicTable.InsertCount;

    /// <summary>
    /// Задача, завершающаяся с приходом новых вставок.
    /// </summary>
    /// <returns>Задача ожидания.</returns>
    /// <remarks>
    /// Нужна потому, что вставки идут ПО ДРУГОМУ потоку соединения, чем ответ, и приходят когда
    /// угодно относительно него. Блок заголовков, сославшийся на ещё не пришедшую запись, — это
    /// не ошибка, а разрешённое нами же состояние: мы объявляем серверу право так делать
    /// параметром QPACK_BLOCKED_STREAMS. Не подождав, мы просто не прочитаем ответ.
    /// </remarks>
    public Task WaitForInsertionsAsync() => Volatile.Read(ref insertions).Task;

    /// <summary>
    /// Разбирает блок заголовков QPACK.
    /// </summary>
    /// <param name="block">Блок заголовков из кадра HEADERS.</param>
    /// <returns>Пары «имя, значение».</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    public IEnumerable<KeyValuePair<string, string>> Decode(ReadOnlySpan<byte> block)
    {
        // Копируем входные данные в массив, чтобы не захватывать stackalloc/Span через yield
        var arr = block.ToArray();
        var r = new BufferReader(arr);
        // ----- Prefix -----
        var encodedRic = HeadersBinaryPrimitives.ReadVarInt(ref r, 8, 0xFF);
        var signAndDeltaPeek = r.PeekByte();
        var sign = (signAndDeltaPeek & 0x80) is not 0;
        var deltaBase = HeadersBinaryPrimitives.ReadVarInt(ref r, 7, 0x7F);
        // ★ Восстановление Required Insert Count по RFC 9204, §4.5.1.1. Значение в блоке
        // закодировано КОЛЬЦЕВЫМ образом: передаётся не сам счётчик, а «остаток по модулю плюс
        // единица», и разворачивать его надо строго по алгоритму спецификации.
        //
        // Прежняя формула ошибалась дважды. Она не вычитала эту единицу, и она сравнивала с
        // KnownReceivedCount — величиной, считающей НАШИ подтверждения серверу, тогда как
        // разворот опирается на число ПРИНЯТЫХ вставок. На пустой или отстающей таблице обе
        // ошибки складывались в отрицательное значение, проверка на блокировку его пропускала
        // (отрицательное больше нуля не бывает), и разбор шёл с заведомо неверной базой. Наружу
        // это выходило сообщением «запись уже вытеснена» — то есть жалобой не на ту причину.
        // Проявлялось на серверах, которые динамической таблицей действительно пользуются:
        // www.google.com по HTTP/3 не открывался вовсе, Cloudflare работал.
        var received = dynamicTable.InsertCount;
        var maxEntries = dynamicTable.MaxEntries;
        int requiredInsertCount;
        int baseValue;

        if (maxEntries > 0 && encodedRic is not 0)
        {
            var fullRange = 2 * maxEntries;
            if (encodedRic > fullRange) throw new InvalidOperationException("QPACK: Required Insert Count вне допустимого диапазона");

            var maxValue = received + maxEntries;
            var maxWrapped = maxValue / fullRange * fullRange;
            var ric = maxWrapped + encodedRic - 1;

            if (ric > maxValue)
            {
                if (ric <= fullRange) throw new InvalidOperationException("QPACK: Required Insert Count не разворачивается");

                ric -= fullRange;
            }

            if (ric is 0) throw new InvalidOperationException("QPACK: Required Insert Count оказался нулевым после разворота");

            requiredInsertCount = ric;

            // Блокировка — штатное состояние: сервер вправе сослаться на вставки, которые ещё в
            // пути по встречному потоку. Ждать их обязан вызывающий.
            if (requiredInsertCount > received) throw new QPackBlockedException(requiredInsertCount);

            baseValue = sign
                ? requiredInsertCount - deltaBase - 1
                : requiredInsertCount + deltaBase;
        }
        else
        {
            baseValue = 0;
        }

        var result = new List<KeyValuePair<string, string>>();

        while (!r.Eof)
        {
            var b = r.PeekByte();
            if (TryProcessRepresentation(ref r, b, baseValue, result))
                continue;

            throw new InvalidOperationException("QPACK: unsupported representation (enable dynamic table path)");
        }

        return result;
    }

    private bool TryProcessRepresentation(ref BufferReader r, byte b, int baseValue, List<KeyValuePair<string, string>> result)
    {
        if (TryProcessIndexedOrIndexedName(ref r, b, baseValue, result)) return true;
        if (TryProcessLiteralOrPostBase(ref r, b, baseValue, result)) return true;
        return false;
    }

    private bool TryProcessIndexedOrIndexedName(ref BufferReader r, byte b, int baseValue, List<KeyValuePair<string, string>> result)
    {
        // Indexed representation (static or dynamic)
        if ((b & 0b1000_0000) is not 0)
        {
            var tStatic = (b & 0b0100_0000) is not 0;
            var index = HeadersBinaryPrimitives.ReadVarInt(ref r, 6, 0x3F);
            if (tStatic)
            {
                var e = QPackStaticTable.Get(index);
                result.Add(new KeyValuePair<string, string>(HPackDecoder.AsciiLowerString(e.Name), HPackDecoder.AsciiString(e.Value)));
            }
            else
            {
                var e = dynamicTable.GetByRelative_RepresentationBase(baseValue, index);
                result.Add(new KeyValuePair<string, string>(HPackDecoder.AsciiLowerString(e.Name), HPackDecoder.AsciiString(e.Value)));
            }

            return true;
        }

        // Literal with name indexed
        if ((b & 0b1100_0000) is 0b0100_0000)
        {
            var nFlag = (b & 0b0010_0000) is not 0;
            var tStatic = (b & 0b0001_0000) is not 0;
            _ = nFlag;
            var nameIndex = HeadersBinaryPrimitives.ReadVarInt(ref r, 4, 0x0F);
            // Имя МАТЕРИАЛИЗУЕМ до чтения значения. Декодировщик Huffman отдаёт срез общего
            // временного буфера, действительный лишь до следующего вызова, — и чтение значения
            // затирает имя. Проявляется это так, что именем заголовка становится начало его же
            // значения: ошибка выглядит как испорченные данные сервера.
            var nameBytes = (tStatic ? QPackStaticTable.Get(nameIndex).Name : dynamicTable.GetByRelative_RepresentationBase(baseValue, nameIndex).Name).ToArray();
            var value = HPackDecoder.ReadStringBytes(ref r).ToArray();
            result.Add(new KeyValuePair<string, string>(HPackDecoder.AsciiLowerString(nameBytes), HPackDecoder.AsciiString(value)));
            return true;
        }

        return false;
    }

    private bool TryProcessLiteralOrPostBase(ref BufferReader r, byte b, int baseValue, List<KeyValuePair<string, string>> result)
    {
        // Literal with literal name
        if ((b & 0b1110_0000) is 0b0010_0000)
        {
            // Имя МАТЕРИАЛИЗУЕМ до чтения значения. Декодировщик Huffman отдаёт срез общего
            // временного буфера, действительный лишь до следующего вызова, — и чтение значения
            // затирает имя. Проявляется это так, что именем заголовка становится начало его же
            // значения: ошибка выглядит как испорченные данные сервера.
            var name = ReadStringBytes(ref r, 3, 0x07, 0x08).ToArray();
            var value = HPackDecoder.ReadStringBytes(ref r).ToArray();
            result.Add(new KeyValuePair<string, string>(HPackDecoder.AsciiLowerString(name), HPackDecoder.AsciiString(value)));
            return true;
        }

        // Post-base indexed
        if ((b & 0b1111_0000) is 0b0001_0000)
        {
            var postIdx = HeadersBinaryPrimitives.ReadVarInt(ref r, 4, 0x0F);
            var e = dynamicTable.GetByAbsolute(baseValue + postIdx);
            result.Add(new KeyValuePair<string, string>(HPackDecoder.AsciiLowerString(e.Name), HPackDecoder.AsciiString(e.Value)));
            return true;
        }

        // Post-base literal name
        if ((b & 0b1111_0000) is 0b0000_0000)
        {
            var nFlag = (b & 0b0000_1000) is not 0;
            _ = nFlag;
            var postNameIdx = HeadersBinaryPrimitives.ReadVarInt(ref r, 3, 0x07);
            // Имя МАТЕРИАЛИЗУЕМ до чтения значения. Декодировщик Huffman отдаёт срез общего
            // временного буфера, действительный лишь до следующего вызова, — и чтение значения
            // затирает имя. Проявляется это так, что именем заголовка становится начало его же
            // значения: ошибка выглядит как испорченные данные сервера.
            var name = dynamicTable.GetByAbsolute(baseValue + postNameIdx).Name.ToArray();
            var value = HPackDecoder.ReadStringBytes(ref r).ToArray();
            result.Add(new KeyValuePair<string, string>(HPackDecoder.AsciiLowerString(name), HPackDecoder.AsciiString(value)));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Чтение строкового литерала QPACK с произвольной шириной префикса.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<byte> ReadStringBytes(ref BufferReader r, int prefixBits, byte prefixMask, byte hBitMask)
    {
        var peek = r.PeekByte();
        var huffman = (peek & hBitMask) != 0;
        var len = HeadersBinaryPrimitives.ReadVarInt(ref r, prefixBits, prefixMask);
        var data = r.ReadSpan(len);

        return huffman ? HPackHuffman.Decode(data) : data;
    }
}