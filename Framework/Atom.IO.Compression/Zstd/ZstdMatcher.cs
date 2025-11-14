using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.IO.Compression.Zstd;

/// <summary>
/// Непревзойдённо простой, но быстрый матчер (одинарный хеш, без цепей) с ограниченной глубиной.
/// </summary>
internal static class ZstdMatcher
{
    private const uint Prime4 = 2654435761u; // 0x9E3779B1, "golden ratio" hash

    /// <summary>
    /// Хеш 4-байтового паттерна по спецификации zstd.
    /// Формула: (v * 2654435761) >> (32 - hashLog).
    /// Возвращает индекс в таблицу размера 2^hashLog.
    /// </summary>
    /// <param name="v">4 байта (LE) исходной последовательности.</param>
    /// <param name="hashLog">Логарифм размера таблицы: 1..31. 32 — недопустимо.</param>
    /// <returns>Индекс в диапазоне [0..(1&lt;&lt;hashLog)-1].</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Hash4(uint v, int hashLog)
    {
        // Защитим контракт и отладочно подсветим ошибку конфигурации: hashLog ожидается в диапазоне [1..31].

        // Внутри соблюдаем "unchecked" для скорости и от отсутствия ложных OverflowException.
        // Сдвиг 32 - hashLog безопасен, т.к. hashLog в [1..31] => сдвиг в [1..31].
        unchecked
        {
            var h = (v * Prime4) >> (32 - hashLog);
            // Преобразование в int безопасно: h < 2^hashLog ≤ 2^31.
            return (int)h;
        }
    }

    /// <summary>
    /// Вариант без внутренних аренд: принимает внешний буфер хеш-таблицы (длина = 1&lt;&lt;p.HashLog), заполнит -1 и переиспользует.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (int seqCount, int literalsSize, int consumed) BuildSequences(
        ReadOnlySpan<byte> src,
        ReadOnlySpan<byte> prefix,
        Span<ZstdSeq> seqs,
        Span<byte> literalsBuffer,
        ZstdMatchParams parameters,
        Span<int> hashTable)
    {
        if (src.IsEmpty) return (0, 0, 0);

        var hashSize = 1 << parameters.HashLog;
        if (hashTable.Length < hashSize)
            throw new ArgumentException("hashTable too small", nameof(hashTable));

        var builder = new SequenceBuildContext(prefix, src, seqs, literalsBuffer, hashTable[..hashSize], parameters);
        return builder.Build();
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct MatchResult
    {
        private MatchResult(bool found, int matchLength, int matchOffset, RepKind repetitionKind)
        {
            Found = found;
            Length = matchLength;
            Offset = matchOffset;
            Rep = repetitionKind;
        }

        public bool Found { get; }
        public int Length { get; }
        public int Offset { get; }
        public RepKind Rep { get; }

        public static MatchResult None => new(found: false, matchLength: 0, matchOffset: 0, repetitionKind: RepKind.None);
        public static MatchResult Create(int matchLength, int matchOffset, RepKind repetitionKind) => new(found: true, matchLength, matchOffset, repetitionKind);
    }

    [StructLayout(LayoutKind.Auto)]
    private ref struct SequenceBuildContext
    {
        private readonly ReadOnlySpan<byte> prefix;
        private readonly ReadOnlySpan<byte> source;
        private readonly Span<ZstdSeq> sequences;
        private readonly Span<byte> literals;
        private readonly Span<int> hash;
        private readonly ZstdMatchParams parameters;
        private readonly int totalLength;
        private readonly int windowSize;
        private readonly int prefixLength;

        private int position;
        private int anchor;
        private int literalPosition;
        private int sequencePosition;
        private uint rep0;
        private uint rep1;
        private uint rep2;

        public SequenceBuildContext(
            ReadOnlySpan<byte> prefix,
            ReadOnlySpan<byte> source,
            Span<ZstdSeq> sequences,
            Span<byte> literals,
            Span<int> hashTable,
            ZstdMatchParams parameters)
        {
            this.prefix = prefix;
            this.source = source;
            this.sequences = sequences;
            this.literals = literals;
            this.parameters = parameters;
            hash = hashTable;

            windowSize = 1 << parameters.WindowLog;
            totalLength = prefix.Length + source.Length;
            prefixLength = prefix.Length;

            position = prefixLength;
            anchor = prefixLength;
            literalPosition = 0;
            sequencePosition = 0;
            rep0 = 1;
            rep1 = 4;
            rep2 = 8;

            hash.Fill(-1);
        }

        public (int seqCount, int literalsSize, int consumed) Build()
        {
            while (ShouldContinue())
            {
                if (!ProcessPosition())
                {
                    break;
                }
            }

            var consumed = anchor - prefixLength;
            if (consumed < 0) consumed = 0;
            return (sequencePosition, literalPosition, consumed);
        }

        private readonly bool ShouldContinue()
        {
            if (sequencePosition >= sequences.Length) return false;
            if ((position + parameters.MinMatch) > totalLength) return false;
            if (position + 4 > totalLength) return false;
            return true;
        }

        private bool ProcessPosition()
        {
            var match = FindMatch();
            if (!match.Found)
            {
                position++;
                return true;
            }

            var matchLength = ExtendMatch(match.Length, match.Offset);
            var literalLength = position - anchor;

            if (!TryCopyLiterals(literalLength, out var storedLiterals))
            {
                return false;
            }

            sequences[sequencePosition++] = new ZstdSeq(storedLiterals, matchLength, match.Offset, match.Rep);
            UpdateRepeatCodes(match.Offset, match.Rep);

            position += matchLength;
            anchor = position;
            return true;
        }

        private readonly MatchResult FindMatch()
        {
            var llZero = position == anchor;
            var bestLength = 0;
            var bestOffset = 0;
            var bestRep = RepKind.None;

            TryRep(prefix, source, position, rep0, RepKind.Rep1, !llZero, totalLength, ref bestLength, ref bestOffset, ref bestRep);
            TryRep(prefix, source, position, rep1, RepKind.Rep2, allowed: true, totalLength, ref bestLength, ref bestOffset, ref bestRep);
            TryRep(prefix, source, position, rep2, RepKind.Rep3, allowed: true, totalLength, ref bestLength, ref bestOffset, ref bestRep);

            if (llZero && rep0 > 1)
            {
                TryRep(prefix, source, position, rep0 - 1, RepKind.Rep1Minus1, allowed: true, totalLength, ref bestLength, ref bestOffset, ref bestRep);
            }

            var value = ReadU32(prefix, source, position);
            var hashIndex = Hash4(value, parameters.HashLog);
            var candidate = hash[hashIndex];
            hash[hashIndex] = position;

            var attempts = parameters.SearchDepth;
            while (attempts-- > 0 && candidate >= 0 && (position - candidate) <= windowSize)
            {
                var matchLength = CommonPrefixLen(prefix, source, position, candidate, totalLength);
                if (matchLength > bestLength)
                {
                    bestLength = matchLength;
                    bestOffset = position - candidate;
                    if (bestLength >= parameters.TargetLength)
                    {
                        break;
                    }
                }

                candidate--;
            }

            return bestLength >= parameters.MinMatch
                ? MatchResult.Create(bestLength, bestOffset, bestRep)
                : MatchResult.None;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void TryRep(
            ReadOnlySpan<byte> prefix,
            ReadOnlySpan<byte> source,
            int position,
            uint offset,
            RepKind kind,
            bool allowed,
            int totalLength,
            ref int bestLen,
            ref int bestOff,
            ref RepKind bestRep)
        {
            if (!allowed) return;
            var matchPos = position - (int)offset;
            if (matchPos < 0 || position + 4 > totalLength || matchPos + 4 > totalLength) return;
            if (ReadU32(prefix, source, position) != ReadU32(prefix, source, matchPos)) return;
            var length = CommonPrefixLen(prefix, source, position, matchPos, totalLength);
            if (length > bestLen)
            {
                bestLen = length;
                bestOff = (int)offset;
                bestRep = kind;
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CommonPrefixLen(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> source, int a, int b, int totalLength)
        {
            var preLength = prefix.Length;
            if (a >= preLength && b >= preLength)
            {
                return CountCommonSame(source, a - preLength, b - preLength);
            }

            if (a < preLength && b < preLength)
            {
                return CountCommonSame(prefix, a, b);
            }

            var maxCross = Math.Min(totalLength - a, totalLength - b);
            var matched = 0;
            while (matched < maxCross && GetByte(prefix, source, a + matched) == GetByte(prefix, source, b + matched))
            {
                matched++;
            }
            return matched;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CountCommonSame(ReadOnlySpan<byte> span, int offsetA, int offsetB)
        {
            if ((uint)offsetA >= (uint)span.Length || (uint)offsetB >= (uint)span.Length)
                return 0;

            var max = span.Length - Math.Max(offsetA, offsetB);
            if (max <= 0) return 0;

            var sliceA = span.Slice(offsetA, max);
            var sliceB = span.Slice(offsetB, max);
            var idx = 0;
            while (idx < max && sliceA[idx] == sliceB[idx]) idx++;
            return idx;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte GetByte(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> source, int index)
            => index < prefix.Length ? prefix[index] : source[index - prefix.Length];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ReadU32(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> source, int index)
        {
            var preLength = prefix.Length;
            if (index >= preLength)
            {
                var offset = index - preLength;
                if (offset + 4 <= source.Length)
                {
                    return Unsafe.ReadUnaligned<uint>(ref Unsafe.AsRef(in source[offset]));
                }
            }
            else if (index + 4 <= preLength)
            {
                return Unsafe.ReadUnaligned<uint>(ref Unsafe.AsRef(in prefix[index]));
            }

            uint b0 = GetByte(prefix, source, index);
            uint b1 = GetByte(prefix, source, index + 1);
            uint b2 = GetByte(prefix, source, index + 2);
            uint b3 = GetByte(prefix, source, index + 3);
            return b0 | (b1 << 8) | (b2 << 16) | (b3 << 24);
        }

        private readonly int ExtendMatch(int initialLength, int offset)
        {
            var length = initialLength;
            while (position + length < totalLength &&
                   (position - offset + length) >= 0 &&
                   GetByte(prefix, source, position + length) == GetByte(prefix, source, position - offset + length))
            {
                length++;
            }
            return length;
        }

        private bool TryCopyLiterals(int literalLength, out int stored)
        {
            stored = 0;
            if (literalLength <= 0) return true;

            var offset = anchor - prefixLength;
            if (offset < 0) offset = 0;

            var copyLength = literalLength - Math.Max(0, prefixLength - anchor);
            if (copyLength <= 0) return true;

            if (literalPosition + copyLength > literals.Length)
            {
                copyLength = literals.Length - literalPosition;
                if (copyLength <= 0) return false;
            }

            source.Slice(offset, copyLength).CopyTo(literals[literalPosition..]);
            literalPosition += copyLength;
            stored = copyLength;
            return true;
        }

        private void UpdateRepeatCodes(int offset, RepKind repKind)
        {
            switch (repKind)
            {
                case RepKind.Rep1:
                    break;
                case RepKind.Rep2:
                    (rep1, rep0) = (rep0, rep1);
                    break;
                case RepKind.Rep3:
                    (rep0, rep2, rep1) = (rep2, rep1, rep0);
                    break;
                default:
                    rep2 = rep1;
                    rep1 = rep0;
                    rep0 = (uint)offset;
                    break;
            }
        }
    }

}
