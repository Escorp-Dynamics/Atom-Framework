using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#pragma warning disable CA2213

namespace Atom.IO.Compression.Zstd;

/// <summary>
/// Рабочее пространство энкодера Zstd. Представляет единый неуправляемый буфер с разбиением на сегменты
/// для всех служебных структур, чтобы избежать повторных аллокаций и копирований.
/// </summary>
internal sealed unsafe class ZstdEncoderWorkspace : IDisposable
{
    internal const int SequenceCapacity = (ZstdStream.MaxRawBlockSize / 4) + 64;
    private static readonly int sequenceBytes = SequenceCapacity * Unsafe.SizeOf<ZstdSeq>();

    private const int TailSize = ZstdStream.MaxRawBlockSize;
    private const int LiteralSize = ZstdStream.MaxRawBlockSize;
    private const int OutputSize = ZstdStream.MaxRawBlockSize * 2;
    private const int DictFourSize = ZstdStream.MaxRawBlockSize * 2;
    private const int DictStreamSize = ZstdStream.MaxRawBlockSize * 4;
    private const int MaxHashLog = 23;
    private const int MaxHashEntries = 1 << MaxHashLog;
    private const int MaxHashBytes = MaxHashEntries * sizeof(int);
    private const int MaxHistoryBytes = ZstdStream.MaxWindowSize;
    private const int DictHuffNbCapacity = 256;
    private const int DictHuffLensCapacity = 256;
    private const int DictHuffCodesCapacity = 256;
    private const int DictNormShortCapacity = 64;

    private static int Align(int value, int alignment) => (value + alignment - 1) & ~(alignment - 1);

    private bool disposed;

    public ZstdEncoderWorkspace()
    {
        var tailOffset = 0;
        var literalOffset = tailOffset + TailSize;
        var outputOffset = literalOffset + LiteralSize;
        var sequencesOffset = outputOffset + OutputSize;
        var dictFourOffset = sequencesOffset + sequenceBytes;
        var dictStreamOffset = dictFourOffset + DictFourSize;
        var hashOffset = Align(dictStreamOffset + DictStreamSize, sizeof(int));
        var historyOffset = Align(hashOffset + MaxHashBytes, sizeof(int));
        var dictHuffNbOffset = Align(historyOffset + MaxHistoryBytes, sizeof(int));
        var dictHuffLensOffset = dictHuffNbOffset + DictHuffNbCapacity;
        var dictHuffCodesOffset = Align(dictHuffLensOffset + DictHuffLensCapacity, sizeof(uint));
        var dictOfNormOffset = Align(dictHuffCodesOffset + (DictHuffCodesCapacity * sizeof(uint)), sizeof(short));
        var dictMlNormOffset = dictOfNormOffset + (DictNormShortCapacity * sizeof(short));
        var dictLlNormOffset = dictMlNormOffset + (DictNormShortCapacity * sizeof(short));
        var totalCapacity = (nuint)Align(dictLlNormOffset + (DictNormShortCapacity * sizeof(short)), sizeof(int));

        var buffer = GC.AllocateUninitializedArray<byte>((int)totalCapacity, pinned: true);

        TailMemory = new Memory<byte>(buffer, tailOffset, TailSize);
        LiteralMemory = new Memory<byte>(buffer, literalOffset, LiteralSize);
        OutputMemory = new Memory<byte>(buffer, outputOffset, OutputSize);
        DictFourMemory = new Memory<byte>(buffer, dictFourOffset, DictFourSize);
        DictStreamMemory = new Memory<byte>(buffer, dictStreamOffset, DictStreamSize);
        HashMemory = new Memory<byte>(buffer, hashOffset, MaxHashBytes);
        HistoryMemory = new Memory<byte>(buffer, historyOffset, MaxHistoryBytes);
        DictionaryHuffNbMemory = new Memory<byte>(buffer, dictHuffNbOffset, DictHuffNbCapacity);
        DictionaryHuffLensMemory = new Memory<byte>(buffer, dictHuffLensOffset, DictHuffLensCapacity);
        DictionaryHuffCodesMemory = new Memory<byte>(buffer, dictHuffCodesOffset, DictHuffCodesCapacity * sizeof(uint));
        DictionaryOffsetsNormMemory = new Memory<byte>(buffer, dictOfNormOffset, DictNormShortCapacity * sizeof(short));
        DictionaryMatchNormMemory = new Memory<byte>(buffer, dictMlNormOffset, DictNormShortCapacity * sizeof(short));
        DictionaryLiteralNormMemory = new Memory<byte>(buffer, dictLlNormOffset, DictNormShortCapacity * sizeof(short));
        SequenceMemory = new Memory<byte>(buffer, sequencesOffset, sequenceBytes);
    }

    private Memory<byte> HashMemory { get; }
    private Memory<byte> HistoryMemory { get; }
    private Memory<byte> DictionaryHuffNbMemory { get; }
    private Memory<byte> DictionaryHuffLensMemory { get; }
    private Memory<byte> DictionaryHuffCodesMemory { get; }
    private Memory<byte> DictionaryOffsetsNormMemory { get; }
    private Memory<byte> DictionaryMatchNormMemory { get; }
    private Memory<byte> DictionaryLiteralNormMemory { get; }
    private Memory<byte> SequenceMemory { get; }

    public Memory<byte> TailMemory { get; }
    public Memory<byte> LiteralMemory { get; }
    public Memory<byte> DictFourMemory { get; }
    public Memory<byte> DictStreamMemory { get; }
    public Memory<byte> OutputMemory { get; }

    public Span<byte> TailSpan => TailMemory.Span;
    public Span<byte> LiteralSpan => LiteralMemory.Span;
    public Span<byte> DictFourSpan => DictFourMemory.Span;
    public Span<byte> DictStreamSpan => DictStreamMemory.Span;
    public Span<byte> OutputSpan => OutputMemory.Span;

    public Span<ZstdSeq> SequenceSpan => MemoryMarshal.Cast<byte, ZstdSeq>(SequenceMemory.Span);

    public Span<byte> DictionaryHuffNbSpan => DictionaryHuffNbMemory.Span;
    public Span<byte> DictionaryHuffLensSpan => DictionaryHuffLensMemory.Span;
    public Span<uint> DictionaryHuffCodesSpan => MemoryMarshal.Cast<byte, uint>(DictionaryHuffCodesMemory.Span);
    public Span<short> DictionaryOffsetsNormSpan => MemoryMarshal.Cast<byte, short>(DictionaryOffsetsNormMemory.Span);
    public Span<short> DictionaryMatchNormSpan => MemoryMarshal.Cast<byte, short>(DictionaryMatchNormMemory.Span);
    public Span<short> DictionaryLiteralNormSpan => MemoryMarshal.Cast<byte, short>(DictionaryLiteralNormMemory.Span);

    public void Reset()
    {
        if (SequenceMemory.Length == 0) return;

        TailSpan.Clear();
        LiteralSpan.Clear();
        DictFourSpan.Clear();
        DictStreamSpan.Clear();
        OutputSpan.Clear();
        SequenceSpan.Clear();
        DictionaryHuffNbSpan.Clear();
        DictionaryHuffLensSpan.Clear();
        DictionaryHuffCodesSpan.Clear();
        DictionaryOffsetsNormSpan.Clear();
        DictionaryMatchNormSpan.Clear();
        DictionaryLiteralNormSpan.Clear();
        HashMemory.Span.Clear();
        HistoryMemory.Span.Clear();
    }

    public Span<int> GetHashSpan(int requiredEntries)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(requiredEntries, MaxHashEntries);
        var requiredBytes = requiredEntries * sizeof(int);
        return MemoryMarshal.Cast<byte, int>(HashMemory.Span[..requiredBytes]);
    }

    public Span<byte> GetHistorySpan(int requiredBytes)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(requiredBytes, MaxHistoryBytes);
        return HistoryMemory.Span[..requiredBytes];
    }

    public Span<byte> GetDictFourSpan(int requiredBytes)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(requiredBytes, DictFourSize);
        return DictFourMemory.Span[..requiredBytes];
    }

    public Span<byte> GetDictStreamSpan(int requiredBytes)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(requiredBytes, DictStreamSize);
        return DictStreamMemory.Span[..requiredBytes];
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        // managed memory will be released by GC
    }
}
