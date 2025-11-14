using System.Numerics;
using System.Runtime.CompilerServices;

namespace Atom.IO.Compression.Zstd;

internal static class HuffmanDecoder
{
    private const int MaxSymbolCount = 256;
    private const int MaxFseLog = 6;
    private const int MaxFseStates = 1 << MaxFseLog;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FloorLog2(int v) => 31 - BitOperations.LeadingZeroCount((uint)v);

    // Build decode table from a list of weights (for symbols 0..last, last weight deduced outside)
    // weightsNB: number_of_bits per symbol (0 -> absent)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void BuildDecodeTable(ReadOnlySpan<byte> weightsNB, int maxBits, Span<byte> symbols, Span<byte> nbBits)
    {
        var tableSize = 1 << maxBits;
        var symbolTable = symbols[..tableSize];
        var nbTable = nbBits[..tableSize];

        // count per length
        Span<int> blCount = stackalloc int[maxBits + 1];
        for (var s = 0; s < weightsNB.Length; s++)
        {
            var l = weightsNB[s];
            if (l != 0) blCount[l]++;
        }

        // canonical codes
        Span<int> nextCode = stackalloc int[maxBits + 1];
        var code = 0;
        for (var bits = 1; bits <= maxBits; bits++)
        {
            code = (code + blCount[bits - 1]) << 1;
            nextCode[bits] = code;
        }

        // init table with default (consume full tableLog to fail early)
        for (var i = 0; i < tableSize; i++) { symbolTable[i] = 0; nbTable[i] = (byte)maxBits; }

        // fill table: for each symbol with length l, assign reversed code and replicate across table
        for (var sym = 0; sym < weightsNB.Length; sym++)
        {
            var l = weightsNB[sym];
            if (l == 0) continue;
            var c = nextCode[l]++;
            // reverse lowest l bits (LSB-first codes)
            var rev = ReverseBits(c, l);
            var stride = 1 << l;
            for (var idx = rev; idx < tableSize; idx += stride)
            {
                symbolTable[idx] = (byte)sym;
                nbTable[idx] = l;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReverseBits(int v, int n)
    {
        // reverse n LSB of v
        v = ((v & 0x5555) << 1) | ((v >> 1) & 0x5555);
        v = ((v & 0x3333) << 2) | ((v >> 2) & 0x3333);
        v = ((v & 0x0F0F) << 4) | ((v >> 4) & 0x0F0F);
        v = ((v & 0x00FF) << 8) | ((v >> 8) & 0x00FF);
        return v >> (16 - n);
    }

    // Build from direct 4-bit weights array (headerByte >= 128 path)
    // weights4bit: each byte encodes two weights: hi4 then lo4; numberOfWeights = headerByte - 127.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HuffmanDecodeTable BuildFromDirectWeights(ReadOnlySpan<byte> weights4bit, int numberOfWeights, ref ZstdDecoderWorkspace.HuffmanTableBlock table)
    {
        // Extract weights for symbols 0..(numberOfWeights-1)
        Span<byte> weights = stackalloc byte[MaxSymbolCount + 1];
        var w = weights[..(numberOfWeights + 1)];
        for (var i = 0; i < numberOfWeights; i++)
        {
            var b = weights4bit[i >> 1];
            var val = ((i & 1) == 0) ? (b >> 4) : (b & 0xF);
            w[i] = (byte)val;
        }

        // deduce last weight: sum 2^(w-1) for w>0 must reach a power of two
        var sum = 0;
        for (var i = 0; i < numberOfWeights; i++)
        {
            var wi = w[i];
            if (wi != 0) sum += 1 << (wi - 1);
        }
        // next power of two
        var pow2 = 1;
        while (pow2 <= sum) pow2 <<= 1;
        var rem = pow2 - sum;
        // rem must be a power of two: weight_last = log2(rem) + 1
        var weightLast = BitOperations.Log2((uint)rem) + 1;
        w[numberOfWeights] = (byte)weightLast;

        // max number of bits = Max_Number_of_Bits = log2(pow2)
        var maxBits = BitOperations.Log2((uint)pow2);
        if (maxBits > 11) throw new InvalidDataException("Huffman depth > 11");

        // Convert weights -> number of bits per symbol
        Span<byte> nbSpan = stackalloc byte[MaxSymbolCount + 1];
        nbSpan = nbSpan[..(numberOfWeights + 1)];
        for (var i = 0; i < nbSpan.Length; i++)
        {
            var wi = w[i];
            nbSpan[i] = (byte)(wi == 0 ? 0 : (maxBits + 1 - wi));
        }

        table.GetWorkspace(out var symWorkspace, out var nbWorkspace);
        BuildDecodeTable(nbSpan, maxBits, symWorkspace, nbWorkspace);
        table.Commit(maxBits);
        return table.ToTable();
    }

    // Decode reverse LE bitstream using table until stream is entirely consumed.
    // Writes up to dst.Length symbols and returns decoded count.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int DecodeStream(ReadOnlySpan<byte> stream, Span<byte> dst, in HuffmanDecodeTable table)
    {
        var outPos = 0;
        var index = stream.Length - 1;
        uint bitContainer = 0;
        var bitCount = 0;
        var mask = (1 << table.TableLog) - 1;
        var symbols = table.Symbols;
        var nbBits = table.NbBits;

        FillBitContainer(ref index, stream, ref bitContainer, ref bitCount);
        if (!SkipPadding(ref index, stream, ref bitContainer, ref bitCount))
            throw new InvalidDataException("Huffman padding marker not found");

        while (TryEnsureBits(table.TableLog, ref index, stream, ref bitContainer, ref bitCount))
        {
            if (outPos >= dst.Length) throw new InvalidDataException("Huffman destination too small");
            var idx = (int)(bitContainer & (uint)mask);
            var nb = nbBits[idx];
            var sym = symbols[idx];
            bitContainer >>= nb;
            bitCount -= nb;
            dst[outPos++] = sym;
        }

        if (bitCount != 0) throw new InvalidDataException("Huffman bitstream not fully consumed");
        return outPos;
    }

    // Decode expecting exact count (single-stream case)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DecodeSingleStream(ReadOnlySpan<byte> stream, Span<byte> dst, in HuffmanDecodeTable table)
    {
        var n = DecodeStream(stream, dst, in table);
        if (n != dst.Length) throw new InvalidDataException("Huffman regenerated size mismatch");
    }

    // Build from FSE-compressed weights (headerByte < 128 path)
    // Format: [FSE header (NCount)] + [bitstream with 2 interleaved states]
    // Returns table and sets consumed to full bytes consumed (should equal compressed size from headerByte at call site).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HuffmanDecodeTable BuildFromFseWeights(ReadOnlySpan<byte> src, ref ZstdDecoderWorkspace.HuffmanTableBlock table, out int consumed)
    {
        Span<short> norm = stackalloc short[16];
        var headerBytes = ParseFseHeader(src, norm, out var tableLog, out var lastSym);

        if (tableLog > MaxFseLog) throw new InvalidDataException("Huffman FSE weights log too large");
        var stateCount = 1 << tableLog;

        Span<byte> fseSymbols = stackalloc byte[MaxFseStates];
        Span<byte> fseNbBits = stackalloc byte[MaxFseStates];
        Span<ushort> fseStateBase = stackalloc ushort[MaxFseStates];
        FseDecoder.Build(norm[..(lastSym + 1)], tableLog, fseSymbols[..stateCount], fseNbBits[..stateCount], fseStateBase[..stateCount]);

        var decoder = FseDecoder.FromTables(tableLog, fseSymbols[..stateCount], fseNbBits[..stateCount], fseStateBase[..stateCount]);

        Span<byte> weights = stackalloc byte[MaxSymbolCount];
        var bodyConsumed = DecodeWeights(src[headerBytes..], ref decoder, tableLog, weights, out var weightCount);

        Span<byte> nbSpan = stackalloc byte[MaxSymbolCount];
        var nbLength = weightCount;
        var maxBits = ConvertWeightsToBitCounts(weights, ref nbLength, nbSpan);

        table.GetWorkspace(out var symWorkspace, out var nbWorkspace);
        BuildDecodeTable(nbSpan[..nbLength], maxBits, symWorkspace, nbWorkspace);

        consumed = headerBytes + bodyConsumed;
        table.Commit(maxBits);
        return table.ToTable();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ParseFseHeader(ReadOnlySpan<byte> source, Span<short> norm, out int tableLog, out int lastSymbol)
    {
        var reader = new ForwardBitReader(source);
        tableLog = (int)reader.ReadBits(4) + 5;
        var remaining = 1 << tableLog;
        var symbol = 0;

        while (remaining > 0 && symbol <= 15)
        {
            if (!TryReadNormalizedCount(ref reader, ref remaining, ref symbol, norm))
            {
                break;
            }
        }

        lastSymbol = symbol - 1;
        return reader.BytesConsumed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryReadNormalizedCount(
        ref ForwardBitReader reader,
        ref int remaining,
        ref int symbol,
        Span<short> norm)
    {
        var maxValue = remaining + 1;
        var bits = FloorLog2(maxValue);
        var threshold = (1 << (bits + 1)) - maxValue;
        var value = reader.ReadBits(bits);
        if (value >= (uint)threshold)
        {
            value = ((value << 1) | reader.ReadBits(1)) - (uint)threshold;
        }

        if (value == 0)
        {
            norm[symbol++] = -1;
            remaining -= 1;
            return true;
        }

        if (value == 1)
        {
            norm[symbol++] = 0;
            var run = 0;
            while (true)
            {
                var extra = (int)reader.ReadBits(2);
                run += extra;
                if (extra != 3) break;
            }

            while (run-- > 0 && symbol <= 15)
            {
                norm[symbol++] = 0;
            }

            return true;
        }

        var probability = (int)value - 1;
        norm[symbol++] = (short)probability;
        remaining -= probability;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DecodeWeights(ReadOnlySpan<byte> body, ref FseDecoder decoder, int tableLog, Span<byte> weights, out int weightCount)
    {
        var reader = new ForwardBitReader(body);
        var state1 = reader.ReadBits(tableLog);
        var state2 = reader.ReadBits(tableLog);
        weightCount = 0;

        while (true)
        {
            if (!TryDecodeWeight(ref decoder, ref reader, ref state1, weights, ref weightCount)) break;
            if (!TryDecodeWeight(ref decoder, ref reader, ref state2, weights, ref weightCount)) break;
        }

        if (weightCount < 255)
        {
            weights[weightCount++] = decoder.PeekSymbol(state1);
            if (weightCount < 255) weights[weightCount++] = decoder.PeekSymbol(state2);
        }

        return reader.BytesConsumed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryDecodeWeight(
        ref FseDecoder decoder,
        ref ForwardBitReader reader,
        ref uint state,
        Span<byte> weights,
        ref int weightCount)
    {
        var symbol = decoder.PeekSymbol(state);
        if (weightCount >= 255) return false;

        weights[weightCount++] = symbol;
        var required = decoder.PeekNbBits(state);
        if (required > reader.AvailableBits) return false;
        var addition = required != 0 ? reader.ReadBits(required) : 0u;
        decoder.UpdateState(ref state, addition);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ConvertWeightsToBitCounts(Span<byte> weights, ref int count, Span<byte> bitCounts)
    {
        var total = 0;
        for (var i = 0; i < count; i++)
        {
            var weight = weights[i];
            if (weight != 0) total += 1 << (weight - 1);
        }

        var pow2 = 1;
        while (pow2 <= total) pow2 <<= 1;
        var remainder = pow2 - total;
        if (remainder <= 0) throw new InvalidDataException("Invalid Huffman weights (sum overflow)");

        var lastWeight = BitOperations.Log2((uint)remainder) + 1;
        if (count >= 256) throw new InvalidDataException("Too many Huffman weights");
        weights[count++] = (byte)lastWeight;

        var maxBits = BitOperations.Log2((uint)pow2);
        if (maxBits > 11) throw new InvalidDataException("Huffman depth > 11");

        for (var i = 0; i < count; i++)
        {
            var weight = weights[i];
            bitCounts[i] = (byte)(weight == 0 ? 0 : (maxBits + 1 - weight));
        }

        return maxBits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FillBitContainer(ref int index, ReadOnlySpan<byte> source, ref uint container, ref int bitCount)
    {
        if (index < 0) return;
        container |= (uint)source[index] << bitCount;
        bitCount += 8;
        index--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SkipPadding(ref int index, ReadOnlySpan<byte> source, ref uint container, ref int bitCount)
    {
        while (true)
        {
            if (bitCount == 0)
            {
                if (index < 0) return false;
                FillBitContainer(ref index, source, ref container, ref bitCount);
                continue;
            }

            var bit = container & 1u;
            container >>= 1;
            bitCount--;
            if (bit == 1u) return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryEnsureBits(int requiredBits, ref int index, ReadOnlySpan<byte> source, ref uint container, ref int bitCount)
    {
        while (bitCount < requiredBits)
        {
            if (index < 0) return false;
            FillBitContainer(ref index, source, ref container, ref bitCount);
        }
        return true;
    }
}
