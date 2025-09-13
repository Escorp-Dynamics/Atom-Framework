using System.Numerics;
using System.Runtime.CompilerServices;

namespace Atom.IO.Compression.Zstd;

internal static class ZstdSeqEncoder
{
    /// <summary>
    /// Записать Sequences Section в dst (без заголовка блока).
    /// Возвращает размер секции в байтах.
    /// Реализовано строго по RFC:
    /// - nbSeq varint
    /// - modes: LL/OF/ML = predef (0)
    /// - без таблиц (predef)
    /// - битстрим: кодируем в ОБРАТНОМ порядке последовательности
    ///   Порядок битов внутри последовательности (обратный к декодеру):
    ///   1) FSE update bits: OF, ML, LL
    ///   2) Additional value bits: LL, ML, OF
    ///   3) В самом конце — начальные состояния (AL бит): ML, OF, LL
    ///   4) финальный bit=1 и нулевой паддинг.
    /// </summary>
    public static int WriteSequences(ReadOnlySpan<ZstdSeq> seqs, Span<byte> dst)
    {
        scoped var bw = new LittleEndianBitWriter(dst);

        var nbSeq = seqs.Length;
        // 1) nbSeq varint
        var headerBytes = WriteNbSeqVarInt(nbSeq, dst);
        var bitstreamOffset = headerBytes;

        // 2) modes: 1 байт — LLType(7..6)=0, OFType(5..4)=0, MLType(3..2)=0, Reserved(1..0)=0
        dst[bitstreamOffset++] = 0; // все predef

        // 3) Подготовка FSE (predef): строим компресс-таблицы
        // LL
        Span<ushort> llStateTable = stackalloc ushort[1 << ZstdLengthsTables.LL_AccuracyLog];
        Span<FseSymbolTransform> llTT = stackalloc FseSymbolTransform[36];
        var fseLL = FseCompressor.Build(ZstdLengthsTables.LL_DefaultNorm, ZstdLengthsTables.LL_AccuracyLog, llStateTable, llTT);

        // ML
        Span<ushort> mlStateTable = stackalloc ushort[1 << ZstdLengthsTables.ML_AccuracyLog];
        Span<FseSymbolTransform> mlTT = stackalloc FseSymbolTransform[53];
        var fseML = FseCompressor.Build(ZstdLengthsTables.ML_DefaultNorm, ZstdLengthsTables.ML_AccuracyLog, mlStateTable, mlTT);

        // OF
        Span<ushort> ofStateTable = stackalloc ushort[1 << ZstdLengthsTables.OffsetsAccuracyLog];
        Span<FseSymbolTransform> ofTT = stackalloc FseSymbolTransform[ZstdLengthsTables.OffsetsMaxN + 1];
        var fseOF = FseCompressor.Build(ZstdLengthsTables.OffsetsDefaultNorm, ZstdLengthsTables.OffsetsAccuracyLog, ofStateTable, ofTT);

        // 4) Инициализация нач. состояний (пока просто выберем "seed" = 0; реальное значение AL бит мы запишем В КОНЦЕ)
        var stateLL = fseLL.InitState(0);
        var stateML = fseML.InitState(0);
        var stateOF = fseOF.InitState(0);
        // "N+state" представление:
        stateLL += 1 << ZstdLengthsTables.LL_AccuracyLog;
        stateML += 1 << ZstdLengthsTables.ML_AccuracyLog;
        stateOF += 1 << ZstdLengthsTables.OffsetsAccuracyLog;

        // 5) Обратный проход по последовательностям: эмитим биты
        // Понадобятся преобразования LL/ML значения -> код + добиты
        for (var i = nbSeq - 1; i >= 0; i--)
        {
            var s = seqs[i];

            // --- LL: значение -> код и добиты
            GetLLCodeBits(s.LL, out var llCode, out var llVal, out var llBits);
            // --- ML:
            GetMLCodeBits(s.ML, out var mlCode, out var mlVal, out var mlBits);
            // --- OF:
            GetOFCodeBits(in s, out var ofCode, out var ofVal, out var ofBits);

            // 1) FSE update bits (в обратном порядке декода): ML → OF → LL
            fseML.EncodeSymbol(ref stateML, mlCode, ref bw);
            fseOF.EncodeSymbol(ref stateOF, ofCode, ref bw);
            fseLL.EncodeSymbol(ref stateLL, llCode, ref bw);

            // 2) Additional value bits (в обратном порядке декода): LL, ML, OF
            if (llBits != 0) bw.WriteBits(llVal, llBits);
            if (mlBits != 0) bw.WriteBits(mlVal, mlBits);
            if (ofBits != 0) bw.WriteBits(ofVal, ofBits);
        }

        // 3) Начальные состояния (AL бит) — в конце битстрима, в обратном порядке чтения: ML, OF, LL
        bw.WriteBits(stateML & ((1u << ZstdLengthsTables.ML_AccuracyLog) - 1u), ZstdLengthsTables.ML_AccuracyLog);
        bw.WriteBits(stateOF & ((1u << ZstdLengthsTables.OffsetsAccuracyLog) - 1u), ZstdLengthsTables.OffsetsAccuracyLog);
        bw.WriteBits(stateLL & ((1u << ZstdLengthsTables.LL_AccuracyLog) - 1u), ZstdLengthsTables.LL_AccuracyLog);

        // 4) Финальный паддинг
        bw.FinishWithOnePadding();

        // Переносим битстрим сразу за заголовок
        var bitstreamSize = bw.BytesWritten;
        // (мы писали непосредственно в dst начиная с bitstreamOffset через bw — так что ничего переносить не нужно)
        return bitstreamOffset + bitstreamSize;
    }

    // ---------- Кодирование LL/ML/OF значений в (code, addBitsVal, addBitsCount)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GetLLCodeBits(int ll, out int code, out uint val, out int nb)
    {
        if (ll <= 15)
        {
            code = ll;
            val = 0;
            nb = 0;
            return;
        }
        // Ищем диапазон по таблице
        var baseTab = ZstdLengthsTables.LLBase;
        var addTab = ZstdLengthsTables.LLAddBits;

        for (var c = 16; c <= 35; c++)
        {
            var baseV = baseTab[c];
            var add = addTab[c];
            var nextBase = (c == 35) ? int.MaxValue : baseTab[c + 1];

            if (ll >= baseV && ll < nextBase)
            {
                code = c;
                var extra = ll - baseV;
                val = (uint)extra;
                nb = add;
                return;
            }
        }
        // За пределами: разбиение LL>65536 не должно встречаться внутри блока 128К.
        code = 35;
        nb = 16;
        val = (uint)Math.Min(0xFFFF, ll - 65536);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GetMLCodeBits(int ml, out int code, out uint val, out int nb)
    {
        if (ml <= 34)
        {
            code = ml - 3;
            val = 0;
            nb = 0;
            return;
        } // 0..31 => ml=3..34

        var baseTab = ZstdLengthsTables.MLBase;
        var addTab = ZstdLengthsTables.MLAddBits;

        for (var c = 32; c <= 52; c++)
        {
            var baseV = baseTab[c];
            var add = addTab[c];
            var nextBase = (c == 52) ? int.MaxValue : baseTab[c + 1];

            if (ml >= baseV && ml < nextBase)
            {
                code = c;
                var extra = ml - baseV;
                val = (uint)extra;
                nb = add;
                return;
            }
        }

        code = 52;
        nb = 16;
        val = (uint)Math.Min(0xFFFF, ml - 65538);
    }

    /// <summary>
    /// OF: оффсет -> (offsetCode, addBits).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void GetOFCodeBits(in ZstdSeq s, out int code, out uint val, out int nb)
    {
        // Repeat-кейсы кодируются значениями Offset_Value 1..3:
        // RFC: при LL>0 — 1->Rep1, 2->Rep2, 3->Rep3;
        // при LL==0 — 1->Rep2, 2->Rep3, 3->Rep1-1 (и это «не-repeat» для истории).
        if (s.Rep != RepKind.None)
        {
            var llZero = s.LL == 0;

            switch (s.Rep)
            {
                case RepKind.Rep1:
                    // при LL=0 прямого кода нет — мы не создаём такие seq в матчере
                    code = 0; nb = 0; val = 0; // LL>0: Offset_Value = 1 -> offsetCode=0
                    return;
                case RepKind.Rep2:
                    if (!llZero) { code = 1; nb = 1; val = 0; }   // value=2
                    else { code = 0; nb = 0; val = 0; }   // LL=0: value=1
                    return;
                case RepKind.Rep3:
                    if (!llZero) { code = 1; nb = 1; val = 1; }   // value=3
                    else { code = 1; nb = 1; val = 0; }   // LL=0: value=2
                    return;
                case RepKind.Rep1Minus1:
                    // всегда кодируется value=3 => offsetCode=1, 1 доп.бит = 1
                    code = 1; nb = 1; val = 1;
                    return;
            }
        }

        // Обычный offset: Offset_Value = (offset+3)
        // offsetCode = floor(log2(Offset_Value)), nb = offsetCode, val = Offset_Value & ((1<<nb)-1)
        var ofv = (uint)(s.Offset + 3);
        var k = 31 - BitOperations.LeadingZeroCount(ofv);
        code = k; nb = k; val = ofv & ((1u << k) - 1u);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int WriteNbSeqVarInt(int nbSeq, Span<byte> dst)
    {
        if (nbSeq == 0)
        {
            dst[0] = 0;
            return 1;
        }

        if (nbSeq < 128)
        {
            dst[0] = (byte)nbSeq;
            return 1;
        }

        if (nbSeq < 0x7F00)
        {
            var v = (nbSeq & 0xFF) | (((nbSeq >> 8) + 128) << 8);
            Unsafe.WriteUnaligned(ref dst[0], (ushort)v);
            return 2;
        }

        dst[0] = 255;
        dst[1] = (byte)(nbSeq & 0xFF);
        dst[2] = (byte)((nbSeq >> 8) & 0xFF);
        return 3;
    }
}