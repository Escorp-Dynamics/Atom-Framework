using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#pragma warning disable CA2213

namespace Atom.IO.Compression.Zstd;

/// <summary>
/// Рабочее пространство декодера Zstd. Выделяет единый неуправляемый буфер, разбитый на сегменты для всех служебных структур,
/// включая окно истории, чтобы исключить повторные аллокации и копирования.
/// </summary>
internal sealed unsafe class ZstdDecoderWorkspace : IDisposable
{
    private const int CompressedSize = ZstdStream.MaxRawBlockSize * 2;
    private const int LiteralSize = ZstdStream.MaxRawBlockSize * 2;
    private const int PendingSize = ZstdStream.MaxRawBlockSize * 2;
    private const int OutputSize = ZstdStream.MaxRawBlockSize * 4;
    private const int InputSize = 32 * 1024;
    private const int HuffmanTableMaxLog = 11;
    private const int HuffmanTableSize = 1 << HuffmanTableMaxLog;
    private const int HuffmanSymbolsSize = HuffmanTableSize;
    private const int HuffmanNbBitsSize = HuffmanTableSize;
    private const int HuffmanSegmentSize = HuffmanSymbolsSize + HuffmanNbBitsSize;
    private const int SeqTableMaxLog = 12;
    private const int SeqTableMaxStates = 1 << SeqTableMaxLog;
    private const int SeqTableEntrySize = (sizeof(byte) * 2) + sizeof(ushort);
    private const int SeqTableSegmentSize = SeqTableMaxStates * SeqTableEntrySize;
    private const int SeqTableSize = SeqTableSegmentSize * 3;
    private const int MaxWindowBytes = ZstdStream.MaxWindowSize;

    private static int Align(int value, int alignment) => (value + alignment - 1) & ~(alignment - 1);

    private readonly Memory<byte> compressedMemory;
    private readonly Memory<byte> literalsMemory;
    private readonly Memory<byte> pendingMemory;
    private readonly Memory<byte> outputMemory;
    private readonly Memory<byte> inputMemory;
    private readonly Memory<byte> windowMemory;

    private HuffmanTableBlock huffmanTable;
    private SequenceTableBlock llTables;
    private SequenceTableBlock mlTables;
    private SequenceTableBlock ofTables;

    public ZstdDecoderWorkspace()
    {
        var compressedOffset = 0;
        var literalsOffset = compressedOffset + CompressedSize;
        var pendingOffset = literalsOffset + LiteralSize;
        var outputOffset = pendingOffset + PendingSize;
        var inputOffset = outputOffset + OutputSize;
        var huffmanOffset = inputOffset + InputSize;
        var sequencesOffset = huffmanOffset + HuffmanSegmentSize;
        var windowOffset = Align(sequencesOffset + SeqTableSize, sizeof(int));
        var totalCapacity = (nuint)Align(windowOffset + MaxWindowBytes, sizeof(int));

        var buffer = GC.AllocateUninitializedArray<byte>((int)totalCapacity, pinned: true);
        var root = (byte*)Unsafe.AsPointer(ref buffer[0]);

        compressedMemory = new Memory<byte>(buffer, compressedOffset, CompressedSize);
        literalsMemory = new Memory<byte>(buffer, literalsOffset, LiteralSize);
        pendingMemory = new Memory<byte>(buffer, pendingOffset, PendingSize);
        outputMemory = new Memory<byte>(buffer, outputOffset, OutputSize);
        inputMemory = new Memory<byte>(buffer, inputOffset, InputSize);
        windowMemory = new Memory<byte>(buffer, windowOffset, MaxWindowBytes);

        huffmanTable = new HuffmanTableBlock(root, huffmanOffset, huffmanOffset + HuffmanSymbolsSize, HuffmanTableSize);
        var seqMemory = new Memory<byte>(buffer, sequencesOffset, SeqTableSize);
        llTables = new SequenceTableBlock(seqMemory[..SeqTableSegmentSize], SeqTableMaxLog);
        mlTables = new SequenceTableBlock(seqMemory.Slice(SeqTableSegmentSize, SeqTableSegmentSize), SeqTableMaxLog);
        ofTables = new SequenceTableBlock(seqMemory.Slice(SeqTableSegmentSize * 2, SeqTableSegmentSize), SeqTableMaxLog);
    }

    public Span<byte> GetCompressedSpan() => compressedMemory.Span;
    public Span<byte> GetLiteralsSpan() => literalsMemory.Span;
    public Span<byte> GetPendingSpan() => pendingMemory.Span;
    public Span<byte> GetOutputSpan() => outputMemory.Span;
    public Span<byte> GetInputSpan() => inputMemory.Span;

    public Memory<byte> GetPendingMemory() => pendingMemory;

    internal ref HuffmanTableBlock HuffmanTable => ref huffmanTable;

    internal ref SequenceTableBlock LiteralLengthTables => ref llTables;

    internal ref SequenceTableBlock MatchLengthTables => ref mlTables;

    internal ref SequenceTableBlock OffsetTables => ref ofTables;

    public void Reset()
    {
        HuffmanTable.Reset();
        LiteralLengthTables.Reset();
        MatchLengthTables.Reset();
        OffsetTables.Reset();
    }

    public Span<byte> AcquireCompressed(int size)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, CompressedSize);
        return compressedMemory.Span[..size];
    }

    public Span<byte> AcquireLiterals(int size)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, LiteralSize);
        return literalsMemory.Span[..size];
    }

    public Span<byte> GetWindowSpan(int required)
    {
        if (required <= 0) return [];
        ArgumentOutOfRangeException.ThrowIfGreaterThan(required, MaxWindowBytes);
        return windowMemory.Span[..required];
    }

    public void Dispose()
    {
        // no managed resources to release
    }

    [StructLayout(LayoutKind.Auto)]
    internal struct HuffmanTableBlock(byte* buffer, int symbolsOffset, int nbBitsOffset, int maxStates)
    {
        private readonly byte* symbolsPtr = buffer + symbolsOffset;
        private readonly byte* nbBitsPtr = buffer + nbBitsOffset;
        private readonly int capacity = maxStates;

        public bool HasTable
        {
            readonly get;
            private set;
        }

        public int TableLog
        {
            readonly get;
            private set;
        }

        public readonly void GetWorkspace(out Span<byte> symbols, out Span<byte> nbBits)
        {
            symbols = new Span<byte>(symbolsPtr, capacity);
            nbBits = new Span<byte>(nbBitsPtr, capacity);
        }

        public readonly ReadOnlySpan<byte> GetSymbols()
        {
            if (!HasTable) throw new InvalidOperationException("Huffman table is not initialized");
            return new ReadOnlySpan<byte>(symbolsPtr, 1 << TableLog);
        }

        public readonly ReadOnlySpan<byte> GetNbBits()
        {
            if (!HasTable) throw new InvalidOperationException("Huffman table is not initialized");
            return new ReadOnlySpan<byte>(nbBitsPtr, 1 << TableLog);
        }

        public void Commit(int tableLog)
        {
            TableLog = tableLog;
            HasTable = true;
        }

        public void Reset()
        {
            HasTable = false;
            TableLog = 0;
        }

        public readonly HuffmanDecodeTable ToTable()
        {
            if (!HasTable) throw new InvalidOperationException("Huffman table is not initialized");
            return new HuffmanDecodeTable(TableLog, symbolsPtr, nbBitsPtr);
        }
    }

    [StructLayout(LayoutKind.Auto)]
    internal struct SequenceTableBlock(Memory<byte> segment, int maxLog)
    {
        private readonly Memory<byte> storage = segment;
        private readonly int maxLogValue = maxLog;
        private readonly int maxStates = 1 << maxLog;

        public bool HasTable
        {
            readonly get;
            private set;
        }

        public int TableLog
        {
            readonly get;
            private set;
        }

        public readonly void GetWritable(int requestedLog, out Span<byte> symbols, out Span<byte> nbBits, out Span<ushort> baseTable)
        {
            if ((uint)requestedLog > (uint)maxLogValue) throw new ArgumentOutOfRangeException(nameof(requestedLog));
            var states = 1 << requestedLog;
            var span = storage.Span;
            var symStorage = span[..maxStates];
            var nbStorage = span.Slice(maxStates, maxStates);
            var baseBytes = span.Slice(maxStates * 2, maxStates * 2);
            var baseStorage = MemoryMarshal.Cast<byte, ushort>(baseBytes);
            symbols = symStorage[..states];
            nbBits = nbStorage[..states];
            baseTable = baseStorage[..states];
        }

        public readonly ReadOnlySpan<byte> GetSymbols()
        {
            if (!HasTable) throw new InvalidOperationException("Sequence table is not initialized");
            return storage.Span[..(1 << TableLog)];
        }

        public readonly ReadOnlySpan<byte> GetNbBits()
        {
            if (!HasTable) throw new InvalidOperationException("Sequence table is not initialized");
            var span = storage.Span;
            return span.Slice(maxStates, maxStates)[..(1 << TableLog)];
        }

        public readonly ReadOnlySpan<ushort> GetBase()
        {
            if (!HasTable) throw new InvalidOperationException("Sequence table is not initialized");
            var baseBytes = storage.Span.Slice(maxStates * 2, maxStates * 2);
            return MemoryMarshal.Cast<byte, ushort>(baseBytes)[..(1 << TableLog)];
        }

        public void Commit(int newTableLog)
        {
            if ((uint)newTableLog > (uint)maxLogValue) throw new ArgumentOutOfRangeException(nameof(newTableLog));
            TableLog = newTableLog;
            HasTable = true;
        }

        public void Reset()
        {
            HasTable = false;
            TableLog = 0;
        }
    }
}
