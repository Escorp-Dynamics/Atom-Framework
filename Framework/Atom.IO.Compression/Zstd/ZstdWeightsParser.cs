using System.Runtime.CompilerServices;
using Atom.IO.Compression.Huffman;

namespace Atom.IO.Compression.Zstd;

/// <summary>
/// Разбор описания дерева литералов Zstandard (RFC 8878, §4.2.1): прямые 4-битные веса
/// и веса, сжатые FSE.
/// </summary>
/// <remarks>
/// Полностью переписан: прежняя версия разбирала FSE-заголовок весов по ошибочной арифметике,
/// возвращала длину заголовка из позиции предзагрузки <c>BitReader</c> (всегда 8 вместо реальных 3–5),
/// читала битовый поток весов ВПЕРЁД вместо обратного направления, безусловно дописывала два
/// «хвостовых» символа и строила дерево каноном DEFLATE. Каждого из этих дефектов по отдельности
/// хватало, чтобы не разобрать ни один настоящий поток zstd.
/// </remarks>
internal static class ZstdWeightsParser
{
    /// <summary>Алфавит весов ограничен 12 (глубина ≤ 11 плюс вес 0) — RFC 8878, §4.2.1.</summary>
    private const int MaxWeightSymbol = 12;

    /// <summary>Предел точности FSE для весов — RFC 8878, §4.2.1.2.</summary>
    private const int MaxWeightsFseLog = 6;

    /// <summary>Максимум явно закодированных весов: последний всегда выводится.</summary>
    private const int MaxExplicitWeights = ZstdHuffman.MaxSymbolCount - 1;

    /// <summary>
    /// Разбирает прямые 4-битные веса (заголовочный байт ≥ 128).
    /// </summary>
    /// <param name="packedWeights">Байты с упакованными весами (старший полубайт первый).</param>
    /// <param name="numberOfWeights">Число явно закодированных весов.</param>
    /// <param name="block">Блок рабочего пространства для таблицы.</param>
    /// <returns>Готовая таблица декодирования.</returns>
    public static HuffmanTable ParseDirectWeights(
        ReadOnlySpan<byte> packedWeights,
        int numberOfWeights,
        ref ZstdDecoderWorkspace.HuffmanTableBlock block)
    {
        if ((uint)numberOfWeights > MaxExplicitWeights) throw new InvalidDataException("Too many Huffman weights");

        Span<byte> weights = stackalloc byte[ZstdHuffman.MaxSymbolCount];
        weights.Clear();

        for (var i = 0; i < numberOfWeights; i++)
        {
            var packed = packedWeights[i >> 1];
            weights[i] = (byte)((i & 1) == 0 ? packed >> 4 : packed & 0xF);
        }

        var count = ZstdHuffman.CompleteWeights(weights, numberOfWeights, out var maxBits);
        return ZstdHuffman.Commit(weights[..count], maxBits, ref block);
    }

    /// <summary>
    /// Разбирает веса, сжатые FSE (заголовочный байт &lt; 128).
    /// </summary>
    /// <param name="source">Ровно те байты, длину которых задал заголовочный байт.</param>
    /// <param name="block">Блок рабочего пространства для таблицы.</param>
    /// <param name="consumed">Сколько байт израсходовано (должно совпасть с длиной источника).</param>
    /// <returns>Готовая таблица декодирования.</returns>
    public static HuffmanTable ParseFseWeights(
        ReadOnlySpan<byte> source,
        ref ZstdDecoderWorkspace.HuffmanTableBlock block,
        out int consumed)
    {
        Span<short> norm = stackalloc short[MaxWeightSymbol + 1];
        var headerBytes = ZstdFseTable.ParseNormalizedCounts(source, MaxWeightSymbol, MaxWeightsFseLog, norm, out var tableLog, out var lastSymbol);
        if (headerBytes >= source.Length) throw new InvalidDataException("Missing Huffman weights bitstream");

        var stateCount = 1 << tableLog;
        Span<byte> fseSymbols = stackalloc byte[1 << MaxWeightsFseLog];
        Span<byte> fseNbBits = stackalloc byte[1 << MaxWeightsFseLog];
        Span<ushort> fseStateBase = stackalloc ushort[1 << MaxWeightsFseLog];
        var decoder = FseDecoder.Build(norm[..(lastSymbol + 1)], tableLog, fseSymbols[..stateCount], fseNbBits[..stateCount], fseStateBase[..stateCount]);

        Span<byte> weights = stackalloc byte[ZstdHuffman.MaxSymbolCount];
        weights.Clear();
        var explicitCount = DecodeWeights(source[headerBytes..], in decoder, tableLog, weights);

        var count = ZstdHuffman.CompleteWeights(weights, explicitCount, out var maxBits);
        consumed = source.Length;
        return ZstdHuffman.Commit(weights[..count], maxBits, ref block);
    }

    /// <summary>
    /// Декодирует последовательность весов из обратного битового потока двумя чередующимися состояниями.
    /// </summary>
    /// <remarks>
    /// RFC 8878, §4.2.1.2: число символов определяется по исчерпанию потока — если для обновления
    /// состояния бит уже не хватает, декодируются символы обоих финальных состояний, и на этом всё.
    /// Прежний код читал поток прямым <c>BitReader</c> с начала блоба и вдобавок безусловно дописывал
    /// два лишних веса после выхода из цикла.
    /// </remarks>
    private static int DecodeWeights(ReadOnlySpan<byte> body, in FseDecoder decoder, int tableLog, Span<byte> weights)
    {
        var reader = new ZstdReverseBitReader(body);
        if (!reader.IsValid) throw new InvalidDataException("Malformed Huffman weights bitstream padding");
        if (!reader.TryReadBits(tableLog, out var state1) || !reader.TryReadBits(tableLog, out var state2))
        {
            throw new InvalidDataException("Truncated Huffman weights bitstream");
        }

        var count = 0;
        while (true)
        {
            Append(weights, ref count, decoder.PeekSymbol(state1));
            if (!TryAdvance(in decoder, ref state1, ref reader))
            {
                Append(weights, ref count, decoder.PeekSymbol(state2));
                break;
            }

            Append(weights, ref count, decoder.PeekSymbol(state2));
            if (!TryAdvance(in decoder, ref state2, ref reader))
            {
                Append(weights, ref count, decoder.PeekSymbol(state1));
                break;
            }
        }

        if (reader.AvailableBits != 0) throw new InvalidDataException("Huffman weights bitstream not fully consumed");
        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Append(Span<byte> weights, ref int count, byte weight)
    {
        if (count >= MaxExplicitWeights) throw new InvalidDataException("Too many Huffman weights");
        weights[count++] = weight;
    }

    /// <summary>
    /// Переводит состояние FSE, если бит на переход ещё хватает.
    /// </summary>
    /// <remarks>
    /// Условие остановки — строгое «бит требуется больше, чем осталось». Переход нулевой ширины
    /// на полностью исчерпанном потоке остаётся ЗАКОННЫМ и обязан выполняться: у весов Хаффмана
    /// вес 0 занимает большую часть состояний таблицы, и там nbBits нередко равен нулю.
    /// Если такие переходы обрывать, серия весов заканчивается на пару символов раньше срока,
    /// последний (выводимый) вес садится не на тот символ, и один-единственный редкий литерал
    /// декодируется в чужой байт — при полностью исчерпанном потоке и без единой другой ошибки.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryAdvance(in FseDecoder decoder, ref uint state, ref ZstdReverseBitReader reader)
    {
        var required = decoder.PeekNbBits(state);
        if (required > reader.AvailableBits) return false;

        reader.TryReadBits(required, out var addition);
        decoder.UpdateState(ref state, addition);
        return true;
    }
}
