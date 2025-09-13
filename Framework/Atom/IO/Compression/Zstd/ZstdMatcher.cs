using System.Buffers;
using System.Runtime.CompilerServices;

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
        // Защитим контракт и отладочно подсветим ошибку конфигурации:
        //Debug.Assert((uint)hashLog <= 31u && (uint)hashLog >= 1u, "hashLog must be in [1..31] for Hash4");

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
    /// Построить последовательности ZSTD внутри блока [src], отдавая:
    /// - literalsBuffer: конкатенация всех литералов (до каждого матча)
    /// - sequences[]: массив команд (LL, ML, Offset)
    /// Возвращает: (кол-во seq, длина literalsStream, последний использованный байт блока).
    /// Важно: "хвост" блока (после последнего матча) оставляем на следующий блок.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (int seqCount, int literalsSize, int consumed) BuildSequences(
        ReadOnlySpan<byte> src,
        Span<ZstdSeq> seqs,
        Span<byte> literalsBuffer,
        ZstdMatchParams p)
    {
        var srcLen = src.Length;
        if (srcLen == 0) return (0, 0, 0);

        var windowSize = 1 << p.WindowLog;
        var hashSize = 1 << p.HashLog;

        // Рентуем хеш-таблицу позиций
        var pool = ArrayPool<int>.Shared;
        var htArray = pool.Rent(hashSize);
        var ht = htArray.AsSpan(0, hashSize);
        ht.Fill(-1);

        var pos = 0;
        var anchor = 0; // начало текущего литерального отрезка
        var litPos = 0; // накопленные литералы
        var seqPos = 0;

        // История повторов смещений (repcodes), инициализация по RFC: 1,4,8
        uint rep0 = 1, rep1 = 4, rep2 = 8;

        try
        {
            while (pos + p.MinMatch <= srcLen && seqPos < seqs.Length)
            {
                // хеш по 4 байтам
                if (pos + 4 > srcLen) break;
                var v = Unsafe.ReadUnaligned<uint>(ref Unsafe.AsRef(in src[pos]));
                var h = Hash4(v, p.HashLog);
                var matchPos = ht[h];
                ht[h] = pos;

                var llZero = pos == anchor;
                var bestLen = 0;
                var bestOff = 0;
                var bestRep = RepKind.None;


                // rep0 / rep1 / rep2 в наших обозначениях: rep0=самый свежий, rep1, rep2
                TryRep(in src, ref pos, rep0, RepKind.Rep1, allow: !llZero, ref srcLen, ref bestLen, ref bestOff, ref bestRep);          // при LL=0 запрещаем прямой Rep1
                TryRep(in src, ref pos, rep1, RepKind.Rep2, allow: true, ref srcLen, ref bestLen, ref bestOff, ref bestRep);
                TryRep(in src, ref pos, rep2, RepKind.Rep3, allow: true, ref srcLen, ref bestLen, ref bestOff, ref bestRep);

                // спец-кейс RFC: Rep2 - 1 byte
                if (llZero && rep2 > 1) TryRep(in src, ref pos, rep2 - 1, RepKind.Rep1Minus1, allow: true, ref srcLen, ref bestLen, ref bestOff, ref bestRep);

                // пробуем repcodes (быстро): rep0, rep1, rep2
                // Отдельная логика, если LL=0 (смещение 1 => "offset_1 - 1_byte") — реализуем чуть позже при кодировании.
                // Здесь просто измеряем потенциальную длину.
                TryMatch(in src, ref pos, rep0, out var ml0);

                if (ml0 >= p.MinMatch)
                {
                    bestLen = ml0;
                    bestOff = (int)rep0;
                }

                TryMatch(in src, ref pos, rep1, out var ml1);

                if (ml1 > bestLen)
                {
                    bestLen = ml1;
                    bestOff = (int)rep1;
                }

                TryMatch(in src, ref pos, rep2, out var ml2);

                if (ml2 > bestLen)
                {
                    bestLen = ml2;
                    bestOff = (int)rep2;
                }

                // дальше — кандидат из хеша и ограниченная глубина (прыжки назад по окну через перехеш на предыдущих позициях)
                var attempts = p.SearchDepth;
                var cand = matchPos;

                while (attempts-- > 0 && cand >= 0 && (pos - cand) <= windowSize)
                {
                    ml2 = CommonPrefixLen(src, pos, cand);
                    if (ml2 > bestLen) { bestLen = ml2; bestOff = pos - cand; if (bestLen >= p.TargetLength) break; }

                    // приблизительное смещение: попробуем «соседний» кандидат (перехеш предыдущей позиции)
                    cand--;
                }

                if (bestLen < p.MinMatch)
                {
                    pos++;
                    continue; // расширяем литеральный отрезок
                }

                // Фиксируем последовательность
                var ll = pos - anchor;
                var ml = bestLen;

                while (pos + ml < srcLen && pos - bestOff + ml >= 0 && src[pos + ml] == src[pos - bestOff + ml]) ml++;

                // записываем литералы и команду
                if (ll > 0) src.Slice(anchor, ll).CopyTo(literalsBuffer[litPos..]);
                litPos += ll;
                seqs[seqPos++] = new ZstdSeq(ll, ml, bestOff, bestRep);

                // обновляем историю по RFC
                switch (bestRep)
                {
                    case RepKind.Rep1: /* история без изменений */ break;
                    case RepKind.Rep2: { (rep1, rep0) = (rep0, rep1); } break;                // swap 1 & 2
                    case RepKind.Rep3: { var t1 = rep0; rep0 = rep2; rep2 = rep1; rep1 = t1; } break; // rotate 3 -> 1
                    default:
                        // "не-repeat": включая Rep1Minus1 (LL=0, value=3), и обычные «длинные» оффсеты
                        rep2 = rep1; rep1 = rep0; rep0 = (uint)bestOff; break;
                }

                pos += ml;
                anchor = pos;
            }

            // ВАЖНО: хвост блока (после последнего матча) НЕ копируем в literals этого блока, иначе декодер их не потребит.
            // Эти байты будут обработаны в следующем блоке (вплоть до блока "только литералы").

            var consumed = anchor; // декодируемый размер = сумма LL+ML всех последовательностей => anchor указывает на конец последнего матча
            return (seqPos, litPos, consumed);

            // --- локалы:
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static int CommonPrefixLen(ReadOnlySpan<byte> a, int i, int j)
            {
                var max = Math.Min(a.Length - i, a.Length - j);
                var k = 0;
                while (k + 8 <= max && Unsafe.ReadUnaligned<ulong>(ref Unsafe.AsRef(in a[i + k])) == Unsafe.ReadUnaligned<ulong>(ref Unsafe.AsRef(in a[j + k]))) k += 8;
                while (k < max && a[i + k] == a[j + k]) k++;
                return k;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static void TryMatch(in ReadOnlySpan<byte> src, ref int pos, uint off, out int len)
            {
                len = 0;
                var mpos = pos - (int)off;
                if (mpos < 0) return;
                if (Unsafe.ReadUnaligned<uint>(ref Unsafe.AsRef(in src[pos])) != Unsafe.ReadUnaligned<uint>(ref Unsafe.AsRef(in src[mpos]))) return;
                len = CommonPrefixLen(src, pos, mpos);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static void TryRep(in ReadOnlySpan<byte> src, ref int pos, uint off, RepKind rk, bool allow, ref int srcLen, ref int bestLen, ref int bestOff, ref RepKind bestRep)
            {
                if (!allow) return;
                var mpos = pos - (int)off;
                if (mpos < 0 || pos + 4 > srcLen || mpos + 4 > srcLen) return;
                if (Unsafe.ReadUnaligned<uint>(ref Unsafe.AsRef(in src[pos])) != Unsafe.ReadUnaligned<uint>(ref Unsafe.AsRef(in src[mpos]))) return;
                var ml = CommonPrefixLen(src, pos, mpos);
                if (ml > bestLen) { bestLen = ml; bestOff = (int)off; bestRep = rk; }
            }
        }
        finally
        {
            pool.Return(htArray, clearArray: true);
        }
    }
}