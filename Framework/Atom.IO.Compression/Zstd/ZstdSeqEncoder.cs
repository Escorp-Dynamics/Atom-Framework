using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
        var bitstreamOffset = PrepareHeader(seqs.Length, dst);
        scoped var bw = new BitWriter(dst[bitstreamOffset..], lsbFirst: true);

        Span<ushort> llStateTable = stackalloc ushort[1 << ZstdLengthsTables.LL_AccuracyLog];
        Span<FseSymbolTransform> llTT = stackalloc FseSymbolTransform[36];
        Span<ushort> mlStateTable = stackalloc ushort[1 << ZstdLengthsTables.ML_AccuracyLog];
        Span<FseSymbolTransform> mlTT = stackalloc FseSymbolTransform[53];
        Span<ushort> ofStateTable = stackalloc ushort[1 << ZstdLengthsTables.OffsetsAccuracyLog];
        Span<FseSymbolTransform> ofTT = stackalloc FseSymbolTransform[ZstdLengthsTables.OffsetsMaxN + 1];

        var llContext = CreateContext(ZstdLengthsTables.LL_DefaultNorm, ZstdLengthsTables.LL_AccuracyLog, llStateTable, llTT);
        var mlContext = CreateContext(ZstdLengthsTables.ML_DefaultNorm, ZstdLengthsTables.ML_AccuracyLog, mlStateTable, mlTT);
        var ofContext = CreateContext(ZstdLengthsTables.OffsetsDefaultNorm, ZstdLengthsTables.OffsetsAccuracyLog, ofStateTable, ofTT);

        if (!TryEncodeSequences(seqs, ref bw, ref llContext, ref mlContext, ref ofContext)) return -1;
        if (!TryWriteInitialStates(ref bw, llContext, mlContext, ofContext)) return -1;

        return FinalizeBitstream(bitstreamOffset, ref bw);
    }

    /// <summary>
    /// Записать Sequences Section с пользовательскими FSE-таблицами (из словаря) для LL/ML/OF.
    /// Если любой из norm-параметров пуст, будет использовано предопределённое распределение.
    /// </summary>
    public static int WriteSequences(
        ReadOnlySpan<ZstdSeq> seqs,
        Span<byte> dst,
        ReadOnlySpan<short> llNorm, int llLog,
        ReadOnlySpan<short> mlNorm, int mlLog,
        ReadOnlySpan<short> ofNorm, int ofLog)
    {
        var bitstreamOffset = PrepareHeader(seqs.Length, dst);
        scoped var bw = new BitWriter(dst[bitstreamOffset..], lsbFirst: true);

        var actualLlLog = ResolveAccuracyLog(llNorm, llLog, ZstdLengthsTables.LL_AccuracyLog);
        var actualMlLog = ResolveAccuracyLog(mlNorm, mlLog, ZstdLengthsTables.ML_AccuracyLog);
        var actualOfLog = ResolveAccuracyLog(ofNorm, ofLog, ZstdLengthsTables.OffsetsAccuracyLog);

        Span<ushort> llStateTable = stackalloc ushort[1 << actualLlLog];
        Span<FseSymbolTransform> llTT = stackalloc FseSymbolTransform[36];
        Span<ushort> mlStateTable = stackalloc ushort[1 << actualMlLog];
        Span<FseSymbolTransform> mlTT = stackalloc FseSymbolTransform[53];
        Span<ushort> ofStateTable = stackalloc ushort[1 << actualOfLog];
        Span<FseSymbolTransform> ofTT = stackalloc FseSymbolTransform[ZstdLengthsTables.OffsetsMaxN + 1];

        var llNormResolved = ResolveNorm(llNorm, ZstdLengthsTables.LL_DefaultNorm);
        var mlNormResolved = ResolveNorm(mlNorm, ZstdLengthsTables.ML_DefaultNorm);
        var ofNormResolved = ResolveNorm(ofNorm, ZstdLengthsTables.OffsetsDefaultNorm);

        var llContext = CreateContext(llNormResolved, actualLlLog, llStateTable, llTT);
        var mlContext = CreateContext(mlNormResolved, actualMlLog, mlStateTable, mlTT);
        var ofContext = CreateContext(ofNormResolved, actualOfLog, ofStateTable, ofTT);

        if (!TryEncodeSequences(seqs, ref bw, ref llContext, ref mlContext, ref ofContext)) return -1;
        if (!TryWriteInitialStates(ref bw, llContext, mlContext, ofContext)) return -1;

        return FinalizeBitstream(bitstreamOffset, ref bw);
    }

    [StructLayout(LayoutKind.Auto)]
    private ref struct SequenceFseContext
    {
        public FseCompressor Compressor;
        public uint State;
        public int AccuracyLog;

        public SequenceFseContext(FseCompressor compressor, uint state, int accuracyLog)
        {
            Compressor = compressor;
            State = state;
            AccuracyLog = accuracyLog;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PrepareHeader(int sequenceCount, Span<byte> dst)
    {
        var offset = WriteNbSeqVarInt(sequenceCount, dst);
        dst[offset] = 0;
        return offset + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SequenceFseContext CreateContext(ReadOnlySpan<short> norm, int accuracyLog, Span<ushort> stateTable, Span<FseSymbolTransform> transforms)
    {
        var compressor = FseCompressor.Build(norm, accuracyLog, stateTable, transforms);
        var initialState = compressor.InitState(0) + (uint)(1 << accuracyLog);
        return new SequenceFseContext(compressor, initialState, accuracyLog);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryEncodeSequences(
        ReadOnlySpan<ZstdSeq> seqs,
        ref BitWriter bw,
        ref SequenceFseContext ll,
        ref SequenceFseContext ml,
        ref SequenceFseContext ofCtx)
    {
        for (var i = seqs.Length - 1; i >= 0; i--)
        {
            if (!EncodeSingleSequence(seqs[i], ref bw, ref ll, ref ml, ref ofCtx)) return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool EncodeSingleSequence(
        in ZstdSeq seq,
        ref BitWriter bw,
        ref SequenceFseContext ll,
        ref SequenceFseContext ml,
        ref SequenceFseContext ofCtx)
    {
        GetLLCodeBits(seq.LL, out var llCode, out var llVal, out var llBits);
        GetMLCodeBits(seq.ML, out var mlCode, out var mlVal, out var mlBits);
        GetOFCodeBits(in seq, out var ofCode, out var ofVal, out var ofBits);

        if (!ofCtx.Compressor.TryEncodeSymbol(ref ofCtx.State, ofCode, ref bw)) return false;
        if (!ml.Compressor.TryEncodeSymbol(ref ml.State, mlCode, ref bw)) return false;
        if (!ll.Compressor.TryEncodeSymbol(ref ll.State, llCode, ref bw)) return false;

        if (llBits != 0 && !bw.TryWriteBits(llVal, llBits)) return false;
        if (mlBits != 0 && !bw.TryWriteBits(mlVal, mlBits)) return false;
        if (ofBits != 0 && !bw.TryWriteBits(ofVal, ofBits)) return false;

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryWriteInitialStates(ref BitWriter bw, SequenceFseContext ll, SequenceFseContext ml, SequenceFseContext ofCtx)
    {
        var mlMask = (1u << ml.AccuracyLog) - 1u;
        var ofMask = (1u << ofCtx.AccuracyLog) - 1u;
        var llMask = (1u << ll.AccuracyLog) - 1u;

        if (!bw.TryWriteBits(ml.State & mlMask, ml.AccuracyLog)) return false;
        if (!bw.TryWriteBits(ofCtx.State & ofMask, ofCtx.AccuracyLog)) return false;
        if (!bw.TryWriteBits(ll.State & llMask, ll.AccuracyLog)) return false;

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FinalizeBitstream(int bitstreamOffset, ref BitWriter bw)
    {
        if (!bw.TryFinishWithPadding()) return -1;
        return bitstreamOffset + bw.BytesWritten;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ResolveAccuracyLog(ReadOnlySpan<short> customNorm, int customLog, int defaultLog)
        => customNorm.IsEmpty ? defaultLog : customLog;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<short> ResolveNorm(ReadOnlySpan<short> customNorm, ReadOnlySpan<short> defaultNorm)
        => customNorm.IsEmpty ? defaultNorm : customNorm;

    // ---------- Кодирование LL/ML/OF значений в (code, addBitsVal, addBitsCount)

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void GetLLCodeBits(int ll, out int code, out uint val, out int nb)
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
    internal static void GetMLCodeBits(int ml, out int code, out uint val, out int nb)
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
    internal static void GetOFCodeBits(in ZstdSeq s, out int code, out uint val, out int nb)
    {
        // Repeat cases reuse the last three offsets according to the RFC:
        // when LL > 0 the codes map to Rep1/Rep2/Rep3; if LL == 0 they map to Rep2/Rep3/(Rep1 - 1).
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
        if (k > ZstdLengthsTables.OffsetsMaxN) k = ZstdLengthsTables.OffsetsMaxN;
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
            // 2-байтовый формат:
            // byte0 = (nbSeq >> 8) + 0x80; byte1 = nbSeq & 0xFF
            dst[0] = (byte)(((nbSeq >> 8) & 0xFF) + 0x80);
            dst[1] = (byte)(nbSeq & 0xFF);
            return 2;
        }

        // 3-байтовый формат:
        dst[0] = 255;
        dst[1] = (byte)(nbSeq & 0xFF);
        dst[2] = (byte)((nbSeq >> 8) & 0xFF);
        return 3;
    }

}
