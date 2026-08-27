using System.Runtime.CompilerServices;
using Atom.Net.Https.Headers.HPack;

namespace Atom.Net.Https.Headers.QPack;

/// <summary>
/// Применяет команды encoder stream (RFC 9204 §4.2) к динамической таблице QPACK.
/// </summary>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0182:Internal type is apparently never used", Justification = "Создаётся декодировщиком QPACK для применения инструкций встречного потока.")]
internal sealed class QPackDecoderStream(QPackDynamicTable t)
{
    private readonly QPackDynamicTable table = t;

    /// <summary>
    /// Разбирает и применяет инструкции встречного потока кодировщика (RFC 9204, §4.3).
    /// </summary>
    /// <param name="stream">Накопленные данные потока.</param>
    /// <returns>Сколько байт разобрано ПОЛНОСТЬЮ.</returns>
    /// <remarks>
    /// ★ Возвращаемая длина — не удобство, а необходимость. Поток кодировщика приходит кусками
    /// QUIC, и кусок вполне может разрезать инструкцию пополам. Прежний разбор об этом не знал:
    /// он читал до конца буфера, а вызывающий выбрасывал кусок целиком — вместе с недочитанной
    /// инструкцией и всем, что шло за ней. Динамическая таблица после этого расходилась с
    /// серверной НАВСЕГДА, а ответ переставал разбираться с жалобой на вытесненную запись.
    ///
    /// Проявлялось это только на серверах, которые таблицей действительно пользуются:
    /// <c>www.google.com</c> по HTTP/3 не открывался вовсе, Cloudflare работал.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SkipLocalsInit]
    public int Apply(ReadOnlySpan<byte> stream)
    {
        var r = new BufferReader(stream);

        while (!r.Eof)
        {
            var complete = r.Position;

            try
            {
                ApplyOne(ref r);
            }
            catch (InvalidOperationException error) when (IsTruncated(error))
            {
                // Инструкция разрезана границей куска: хвост остаётся вызывающему.
                return complete;
            }
        }

        return r.Position;
    }

    /// <summary>
    /// Отличает нехватку данных от настоящей ошибки протокола.
    /// </summary>
    /// <param name="error">Пойманное исключение.</param>
    /// <returns><see langword="true"/>, если данных просто не хватило.</returns>
    private static bool IsTruncated(InvalidOperationException error)
        => error.Message.Contains("недостаёт данных", StringComparison.Ordinal);

    /// <summary>
    /// Применяет одну инструкцию.
    /// </summary>
    /// <param name="r">Читатель потока.</param>
    private void ApplyOne(ref BufferReader r)
    {
        var b = r.PeekByte();

        if ((b & 0b1000_0000) != 0)
        {
            // 1 T NameIdx(6+) + Value — вставка со ссылкой на имя.
            var tStatic = (b & 0b0100_0000) != 0;
            var nameIndex = HeadersBinaryPrimitives.ReadVarInt(ref r, 6, 0x3F);

            // Имя МАТЕРИАЛИЗУЕМ до чтения значения: декодировщик Huffman отдаёт срез общего
            // временного буфера, и чтение значения его затирает.
            var name = (tStatic
                ? QPackStaticTable.Get(nameIndex).Name
                : table.GetByRelative_EncoderStream(nameIndex).Name).ToArray();

            var value = HPackDecoder.ReadStringBytes(ref r).ToArray();

            table.Add(name, value);
            table.OnInsertCountIncrement(1);
            return;
        }

        if ((b & 0b1100_0000) == 0b0100_0000)
        {
            // 01 N H NameLen(5+) + Name + H ValueLen(7+) + Value — вставка без ссылки на имя.
            var name = ReadStringBytes(ref r, 5, 0x1F, 0x20).ToArray();
            var value = HPackDecoder.ReadStringBytes(ref r).ToArray();

            table.Add(name, value);
            table.OnInsertCountIncrement(1);
            return;
        }

        if ((b & 0b1111_0000) == 0b0001_0000)
        {
            // 0001 Index(4+) — повтор записи.
            var relative = HeadersBinaryPrimitives.ReadVarInt(ref r, 4, 0x0F);

            table.Duplicate(relative);
            table.OnInsertCountIncrement(1);
            return;
        }

        if ((b & 0b1110_0000) == 0b0010_0000)
        {
            // 001 Capacity(5+) — установка ёмкости таблицы; счётчик вставок не растёт.
            table.SetCapacity(HeadersBinaryPrimitives.ReadVarInt(ref r, 5, 0x1F));
            return;
        }

        throw new InvalidOperationException("QPACK encoder stream: неизвестный тип инструкции");
    }

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