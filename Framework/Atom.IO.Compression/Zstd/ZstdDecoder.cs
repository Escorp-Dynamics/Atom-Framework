#pragma warning disable CA2213

using System.Buffers.Binary;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Atom.IO.Compression.Huffman;
using Atom.IO.Compression.Zstd;

namespace Atom.IO.Compression;

/// <summary>
/// Внутренний декодер Zstd: RAW/RLE блоки, skippable‑фреймы, Content Checksum. Без сторонних библиотек.
/// </summary>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
internal sealed class ZstdDecoder([NotNull] System.IO.Stream input, IZstdDictionaryProvider? dictionaryProvider = null) : IDisposable
{
    private readonly System.IO.Stream stream = input;
    private readonly IZstdDictionaryProvider? dictProvider = dictionaryProvider;

    // Состояние кадра
    private bool inFrame;
    private bool hasChecksum;

    // Состояние блока
    private ZstdBlockKind blockKind = ZstdBlockKind.None;
    private int blockRemaining;
    private byte rleValue;
    private bool lastBlock;

    private XxHash64 hash = new();
    private bool isDisposed;

    // Окно истории (ring buffer) для матчей; будет использовано для Compressed-блоков
    private int windowSize;
    private int winPos;
    private int winFill;
    private bool isWindowInitialized;
    private Span<byte> WindowSpan => workspace.GetWindowSpan(windowSize);

    // История повторных смещений между Compressed-блоками
    private uint rep1 = 1, rep2 = 4, rep3 = 8;

    // Очередь готового вывода (для Compressed-блоков)
    private int pendingPos;
    private int pendingLen;
    private readonly ZstdDecoderWorkspace workspace = ZstdDecoderWorkspacePool.Rent();
    private bool workspaceReturned;

    // FSE таблицы для Repeat_Mode (переиспользуются между Compressed-блоками)
    private ref ZstdDecoderWorkspace.SequenceTableBlock LlTables => ref workspace.LiteralLengthTables;
    private ref ZstdDecoderWorkspace.SequenceTableBlock MlTables => ref workspace.MatchLengthTables;
    private ref ZstdDecoderWorkspace.SequenceTableBlock OfTables => ref workspace.OffsetTables;
    private bool hasSeqTables;
    // Запоминаем используемые tableLog'и для Repeat_Mode
    private int lastLlLog = ZstdLengthsTables.LL_AccuracyLog;
    private int lastMlLog = ZstdLengthsTables.ML_AccuracyLog;
    private int lastOfLog = ZstdLengthsTables.OffsetsAccuracyLog;

    // Huffman таблица литералов (для Treeless)
    private bool hasHuffTable;
    // Словарь (отложенное применение после инициализации окна)
    private ReadOnlyMemory<byte> pendingDictContent;
    private bool hasPendingDict;
    private uint dictRep1, dictRep2, dictRep3;

    // Буферизированный ввод для снижения количества Read()
    private int inPos, inLen;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Read(Span<byte> dst)
    {
        if (dst.Length == 0) return 0;

        var written = 0;
        while (written < dst.Length)
        {
            if (pendingLen > 0)
            {
                written += DrainPending(dst[written..]);
                continue;
            }

            if (!EnsureBlockReady())
            {
                break;
            }

            if (blockKind == ZstdBlockKind.None)
            {
                continue;
            }

            var target = dst[written..];
            written += blockKind switch
            {
                ZstdBlockKind.Raw => CopyRawBlock(target),
                ZstdBlockKind.Rle => EmitRleBlock(target),
                _ => throw new NotSupportedException("Compressed blocks are not supported yet"),
            };
        }

        return written;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int DrainPending(Span<byte> destination)
    {
        var pendingSpan = workspace.GetPendingSpan();
        var toCopy = Math.Min(destination.Length, pendingLen);
        pendingSpan.Slice(pendingPos, toCopy).CopyTo(destination[..toCopy]);
        pendingPos += toCopy;
        pendingLen -= toCopy;
        if (pendingLen == 0) pendingPos = 0;
        return toCopy;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool EnsureBlockReady()
    {
        while (true)
        {
            if (blockKind != ZstdBlockKind.None && blockRemaining > 0) return true;

            if (!inFrame && !TryStartNextFrame()) return false;

            TryBeginNextBlock();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CopyRawBlock(Span<byte> destination)
    {
        var toRead = Math.Min(blockRemaining, destination.Length);
        var span = destination[..toRead];
        if (!ReadExact(span)) throw new InvalidDataException("Unexpected EOF inside RAW block");
        if (hasChecksum) hash.Update(span);
        AppendToWindow(span);

        blockRemaining -= toRead;
        if (blockRemaining == 0 && lastBlock) FinishFrame();

        return toRead;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int EmitRleBlock(Span<byte> destination)
    {
        var toWrite = Math.Min(blockRemaining, destination.Length);
        var target = destination[..toWrite];
        target.Fill(rleValue);
        if (hasChecksum) hash.UpdateRepeat(rleValue, toWrite);
        AppendRepeatToWindow(rleValue, toWrite);

        blockRemaining -= toWrite;
        if (blockRemaining == 0 && lastBlock) FinishFrame();

        return toWrite;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<int> ReadAsync(Memory<byte> dst, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(Read(dst.Span));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref isDisposed, value: true, default)) return;
        ReleaseWorkspace();
    }

    private void ReleaseWorkspace()
    {
        if (workspaceReturned) return;
        workspace.Reset();
        ZstdDecoderWorkspacePool.Return(workspace);
        workspaceReturned = true;
        inPos = 0;
        inLen = 0;
    }

    // ------------ Внутренности ------------
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryStartNextFrame()
    {
        Span<byte> header = stackalloc byte[4];
        Span<byte> lengthBuffer = stackalloc byte[4];
        while (TryReadFrameMagic(header, out var magic))
        {
            if (TryBeginFrame(magic)) return true;
            if (TrySkipSkippableFrame(magic, lengthBuffer)) continue;
            throw new InvalidDataException("Unknown magic");
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryReadFrameMagic(Span<byte> header, out uint magic)
    {
        if (!ReadExact(header))
        {
            magic = 0;
            return false;
        }

        magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryBeginFrame(uint magic)
    {
        if (magic != ZstdStream.FrameMagic) return false;

        ParseFrameHeader();
        InitializeFrameState();
        return true;
    }

    private void InitializeFrameState()
    {
        inFrame = true;
        if (hasChecksum) hash = new XxHash64();
        rep1 = 1; rep2 = 4; rep3 = 8;
        if (!hasPendingDict) return;
        if (dictRep1 == 0 || dictRep2 == 0 || dictRep3 == 0) return;
        rep1 = dictRep1;
        rep2 = dictRep2;
        rep3 = dictRep3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TrySkipSkippableFrame(uint magic, Span<byte> lengthBuffer)
    {
        if ((magic & 0xFFFFFFF0u) != ZstdStream.SkippableBase) return false;
        if (!ReadExact(lengthBuffer)) throw new InvalidDataException("Truncated skippable size");
        var size = BinaryPrimitives.ReadUInt32LittleEndian(lengthBuffer);
        SkipBytes(size);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ParseFrameHeader()
    {
        Span<byte> buffer = stackalloc byte[14];
        var descriptor = ReadFrameDescriptor(buffer);
        var fcsId = (descriptor >> 6) & 0x3;
        var singleSegment = ((descriptor >> 5) & 1) != 0;
        var hasDictionary = (descriptor & 0x3) != 0;

        if (!singleSegment)
        {
            windowSize = ReadWindowSize(buffer);
        }

        if (hasDictionary)
        {
            ReadAndApplyDictionary(descriptor & 0x3, buffer);
        }

        var frameContentSize = ReadFrameContentSize(fcsId, singleSegment, buffer);
        if (singleSegment)
        {
            windowSize = ClampSingleSegmentWindow(frameContentSize);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte ReadFrameDescriptor(Span<byte> buffer)
    {
        if (!ReadExact(buffer[..1])) throw new InvalidDataException("Truncated FHD");
        var descriptor = buffer[0];
        var reservedBit = (descriptor >> 3) & 1;
        hasChecksum = ((descriptor >> 2) & 1) != 0;
        if (reservedBit != 0) throw new InvalidDataException("Reserved bit must be 0");
        return descriptor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ReadWindowSize(Span<byte> buffer)
    {
        if (!ReadExact(buffer[..1])) throw new InvalidDataException("Truncated WD");
        return DecodeWindowDescriptor(buffer[0]);
    }

    private void ReadAndApplyDictionary(int dictionaryDescriptor, Span<byte> buffer)
    {
        var size = dictionaryDescriptor switch
        {
            1 => 1,
            2 => 2,
            3 => 4,
            _ => throw new InvalidDataException("Invalid dictionary id descriptor"),
        };

        if (!ReadExact(buffer[..size])) throw new InvalidDataException("Truncated DID");
        var did = size switch
        {
            1 => buffer[0],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(buffer[..2]),
            4 => BinaryPrimitives.ReadUInt32LittleEndian(buffer[..4]),
            _ => 0u,
        };

        if (dictProvider is null) throw new NotSupportedException("Dictionary frames are not supported");
        if (!dictProvider.TryGet(did, out var dictBytes)) throw new InvalidDataException("Dictionary not found for DID");
        ParseDictionary(dictBytes, did);
    }

    private ulong ReadFrameContentSize(int fcsId, bool singleSegment, Span<byte> buffer)
    {
        if (singleSegment)
        {
            var bytesToRead = GetSingleSegmentFcsLength(fcsId);
            return ReadFrameSizeValue(buffer, bytesToRead);
        }

        if (fcsId == 0) return 0;
        var multiSegmentLength = GetMultiSegmentFcsLength(fcsId);
        return ReadFrameSizeValue(buffer, multiSegmentLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ClampSingleSegmentWindow(ulong frameSize)
    {
        var window = (long)frameSize;
        if (window < 1024) window = 1024;
        if (window > ZstdStream.MaxWindowSize) window = ZstdStream.MaxWindowSize;
        return (int)window;
    }

    private static int GetSingleSegmentFcsLength(int fcsId) => fcsId switch
    {
        0 => 1,
        1 => 2,
        2 => 4,
        3 => 8,
        _ => throw new InvalidDataException("Invalid FCS size"),
    };

    private static int GetMultiSegmentFcsLength(int fcsId) => fcsId switch
    {
        1 => 2,
        2 => 4,
        3 => 8,
        _ => throw new InvalidDataException("Invalid FCS size"),
    };

    private ulong ReadFrameSizeValue(Span<byte> buffer, int length)
    {
        if (length <= 0) throw new InvalidDataException("Invalid FCS size");
        if (!ReadExact(buffer[..length])) throw new InvalidDataException("Truncated FCS");

        var value = length switch
        {
            1 => buffer[0],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(buffer[..2]) + 256u,
            4 => BinaryPrimitives.ReadUInt32LittleEndian(buffer[..4]),
            8 => BinaryPrimitives.ReadUInt64LittleEndian(buffer[..8]),
            _ => 0u,
        };

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryBeginNextBlock()
    {
        Span<byte> h = stackalloc byte[3];
        if (!ReadExact(h)) return false;
        var header = (uint)(h[0] | (h[1] << 8) | (h[2] << 16));
        lastBlock = (header & 1) != 0;
        var type = (int)((header >> 1) & 0x3);
        var size = (int)(header >> 3);

        switch (type)
        {
            case 0:
                blockKind = ZstdBlockKind.Raw; blockRemaining = size; break;
            case 1:
                {
                    Span<byte> v = stackalloc byte[1];
                    if (!ReadExact(v)) throw new InvalidDataException("Truncated RLE byte");
                    rleValue = v[0]; blockKind = ZstdBlockKind.Rle; blockRemaining = size; break;
                }
            case 2:
                DecompressCompressedBlock(size);
                blockKind = ZstdBlockKind.None; // вся распаковка в pending
                if (lastBlock) FinishFrame();
                return true;
            default:
                throw new InvalidDataException("Invalid block type");
        }

        if (blockRemaining == 0 && lastBlock)
        {
            FinishFrame();
            return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FinishFrame()
    {
        if (hasChecksum)
        {
            Span<byte> c = stackalloc byte[4];
            if (!ReadExact(c)) throw new InvalidDataException("Truncated checksum");
            var got = BinaryPrimitives.ReadUInt32LittleEndian(c);
            var exp = unchecked((uint)hash.Digest());
            if (got != exp) throw new InvalidDataException("Content checksum mismatch");
        }

        inFrame = false;
        blockKind = ZstdBlockKind.None;
        blockRemaining = 0;
        lastBlock = false;
        hasPendingDict = false;
        // Освобождаем окно истории
        winPos = 0;
        winFill = 0;
        windowSize = 0;
        isWindowInitialized = false;
        // Сбрасываем Huffman таблицу
        workspace.HuffmanTable.Reset();
        hasHuffTable = false;
        inPos = 0;
        inLen = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ReadExact(Span<byte> dst)
    {
        var remaining = dst.Length;
        var offset = 0;
        var inputBuffer = workspace.GetInputSpan();
        while (remaining > 0)
        {
            var buffered = inLen - inPos;
            if (buffered > 0)
            {
                var take = Math.Min(remaining, buffered);
                inputBuffer.Slice(inPos, take).CopyTo(dst.Slice(offset, take));
                inPos += take;
                offset += take;
                remaining -= take;
                continue;
            }

            if (remaining >= inputBuffer.Length)
            {
                var span = dst.Slice(offset, remaining);
                var read = stream.Read(span);
                if (read <= 0) return false;
                offset += read;
                remaining -= read;
                continue;
            }

            var readCount = stream.Read(inputBuffer);
            if (readCount <= 0) return false;
            inPos = 0;
            inLen = readCount;
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SkipBytes(uint count)
    {
        var left = count;
        var inputBuffer = workspace.GetInputSpan();
        while (left > 0)
        {
            var buffered = inLen - inPos;
            if (buffered > 0)
            {
                var take = Math.Min((int)left, buffered);
                inPos += take;
                left -= (uint)take;
                continue;
            }

            var readCount = stream.Read(inputBuffer);
            if (readCount <= 0) throw new InvalidDataException("Unexpected EOF while skipping");

            if ((uint)readCount > left)
            {
                inPos = (int)left;
                inLen = readCount;
                left = 0;
            }
            else
            {
                left -= (uint)readCount;
                inPos = 0;
                inLen = 0;
            }
        }
    }

    // ---- Compressed Block (Predefined modes only; Literals RAW/RLE only)
    [StructLayout(LayoutKind.Auto)]
    private readonly ref struct LiteralsInfo
    {
        public LiteralsInfo(int headerLength, int sectionLength, int size, bool isRle, byte rleValue, bool usesWorkspace, ReadOnlySpan<byte> rawData)
        {
            HeaderLength = headerLength;
            SectionLength = sectionLength;
            Size = size;
            IsRle = isRle;
            RleValue = rleValue;
            UsesWorkspace = usesWorkspace;
            RawData = rawData;
        }

        public int HeaderLength { get; }
        public int SectionLength { get; }
        public int Size { get; }
        public bool IsRle { get; }
        public byte RleValue { get; }
        public bool UsesWorkspace { get; }
        public ReadOnlySpan<byte> RawData { get; }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<byte> GetLiteralData(in LiteralsInfo info)
        => info.UsesWorkspace ? workspace.GetLiteralsSpan()[..info.Size] : info.RawData;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<byte> GetLiteralSlice(in LiteralsInfo info, int offset, int length)
        => GetLiteralData(info).Slice(offset, length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LiteralsInfo DecodeLiteralsSection(ReadOnlySpan<byte> body, ref ZstdDecoderWorkspace.HuffmanTableBlock huffmanBlock)
    {
        if (body.IsEmpty) throw new InvalidDataException("Empty compressed block body");
        var ltype = body[0] & 0x3;
        return ltype switch
        {
            0 or 1 => DecodeRawOrRleLiterals(body),
            2 => DecodeCompressedLiterals(body, ref huffmanBlock, literalType: 2),
            3 => DecodeCompressedLiterals(body, ref huffmanBlock, literalType: 3),
            _ => throw new InvalidDataException("Unsupported literals section type"),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LiteralsInfo DecodeRawOrRleLiterals(ReadOnlySpan<byte> body)
    {
        ParseLiteralsSectionHeader(body, out var headerLen, out var regeneratedSize, out var isRle);
        if (isRle)
        {
            if (body.Length < headerLen + 1) throw new InvalidDataException("Truncated RLE literals");
            var sectionLength = headerLen + 1;
            return new LiteralsInfo(headerLen, sectionLength, regeneratedSize, isRle: true, body[headerLen], usesWorkspace: false, rawData: []);
        }

        if (body.Length < headerLen + regeneratedSize) throw new InvalidDataException("Truncated RAW literals body");
        var data = body.Slice(headerLen, regeneratedSize);
        return new LiteralsInfo(headerLen, headerLen + regeneratedSize, regeneratedSize, isRle: false, default, usesWorkspace: false, rawData: data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LiteralsInfo DecodeCompressedLiterals(ReadOnlySpan<byte> body, ref ZstdDecoderWorkspace.HuffmanTableBlock huffmanBlock, int literalType)
    {
        ParseCompressedLiteralsHeader(body, out var headerLen, out var regeneratedSize, out var compressedSize, out var isFourStreams);
        if (body.Length < headerLen + compressedSize) throw new InvalidDataException("Truncated compressed literals body");
        var payload = body.Slice(headerLen, compressedSize);
        if (payload.IsEmpty) throw new InvalidDataException("Empty compressed literals payload");

        var table = BuildHuffmanTable(payload, literalType, ref huffmanBlock, out var streams);
        var target = workspace.GetLiteralsSpan();
        DecodeHuffmanStreams(streams, isFourStreams, target[..regeneratedSize], regeneratedSize, in table);
        return new LiteralsInfo(headerLen, headerLen + compressedSize, regeneratedSize, isRle: false, default, usesWorkspace: true, rawData: []);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private HuffmanTable BuildHuffmanTable(
        ReadOnlySpan<byte> payload,
        int literalType,
        ref ZstdDecoderWorkspace.HuffmanTableBlock huffmanBlock,
        out ReadOnlySpan<byte> streams)
    {
        if (literalType == 2)
        {
            return BuildHuffmanTableFromPayload(payload, ref huffmanBlock, out streams);
        }

        if (!TryGetHuffmanTable(out var table))
            throw new InvalidDataException("Treeless without prior Huffman table");

        streams = payload;
        return table;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private HuffmanTable BuildHuffmanTableFromPayload(
        ReadOnlySpan<byte> payload,
        ref ZstdDecoderWorkspace.HuffmanTableBlock huffmanBlock,
        out ReadOnlySpan<byte> streams)
    {
        var headerByte = payload[0];
        if (headerByte < 128)
        {
            var weightsSize = headerByte;
            if (payload.Length < 1 + weightsSize) throw new InvalidDataException("Truncated FSE-compressed weights");
            var weights = payload.Slice(1, weightsSize);
            var table = ZstdWeightsParser.ParseFseWeights(weights, ref huffmanBlock, out var consumedBytes);
            if (consumedBytes != weightsSize) throw new InvalidDataException("Huffman weights size mismatch");
            StoreHuffmanTable();
            streams = payload[(1 + weightsSize)..];
            return table;
        }
        else
        {
            var numberOfWeights = headerByte - 127;
            var bytesNeeded = (numberOfWeights + 1) / 2;
            if (payload.Length < 1 + bytesNeeded) throw new InvalidDataException("Truncated direct weights");
            var weights = payload.Slice(1, bytesNeeded);
            var result = ZstdWeightsParser.ParseDirectWeights(weights, numberOfWeights, ref huffmanBlock);
            StoreHuffmanTable();
            streams = payload[(1 + bytesNeeded)..];
            return result;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DecodeHuffmanStreams(ReadOnlySpan<byte> streams, bool isFourStreams, Span<byte> destination, int regeneratedSize, in HuffmanTable table)
    {
        if (!isFourStreams)
        {
            HuffmanDecoder.DecodeReverseStreamExact(streams, destination, in table);
            return;
        }

        if (streams.Length < 6) throw new InvalidDataException("Truncated Huffman jump table");
        var s1 = (int)BinaryPrimitives.ReadUInt16LittleEndian(streams[..2]);
        var s2 = (int)BinaryPrimitives.ReadUInt16LittleEndian(streams.Slice(2, 2));
        var s3 = (int)BinaryPrimitives.ReadUInt16LittleEndian(streams.Slice(4, 2));
        var total = streams.Length - 6;
        var s4 = total - (s1 + s2 + s3);
        if (s4 < 0) throw new InvalidDataException("Invalid Huffman streams sizes");
        var off1 = 6;
        var off2 = off1 + s1;
        var off3 = off2 + s2;
        var off4 = off3 + s3;
        if (off4 + s4 != streams.Length) throw new InvalidDataException("Huffman streams size mismatch");
        var written = 0;
        written += HuffmanDecoder.DecodeReverseStream(streams.Slice(off1, s1), destination[written..], in table);
        written += HuffmanDecoder.DecodeReverseStream(streams.Slice(off2, s2), destination[written..], in table);
        written += HuffmanDecoder.DecodeReverseStream(streams.Slice(off3, s3), destination[written..], in table);
        written += HuffmanDecoder.DecodeReverseStream(streams.Slice(off4, s4), destination[written..], in table);
        if (written != regeneratedSize) throw new InvalidDataException("Huffman regenerated size mismatch");
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly ref struct SequenceHeader(
        int nbSeq,
        int llMode,
        int ofMode,
        int mlMode,
        byte llRle,
        byte ofRle,
        byte mlRle,
        ReadOnlySpan<byte> tableData,
        int headerLength,
        ReadOnlySpan<byte> bitstream)
    {
        public int NbSeq { get; } = nbSeq;
        public int LlMode { get; } = llMode;
        public int OfMode { get; } = ofMode;
        public int MlMode { get; } = mlMode;
        public byte LlRle { get; } = llRle;
        public byte OfRle { get; } = ofRle;
        public byte MlRle { get; } = mlRle;
        public ReadOnlySpan<byte> TableData { get; } = tableData;
        public int FseHeaderLength { get; } = headerLength;
        public ReadOnlySpan<byte> Bitstream { get; } = bitstream;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SequenceHeader ParseSequenceHeader(ReadOnlySpan<byte> seq)
    {
        ParseNbSeq(seq, out var nbSeq, out var nbHdrLen);
        if (seq.Length < nbHdrLen + 1) throw new InvalidDataException("Truncated modes byte");

        var modes = seq[nbHdrLen];
        var llMode = (modes >> 6) & 0x3;
        var ofMode = (modes >> 4) & 0x3;
        var mlMode = (modes >> 2) & 0x3;

        var tableData = seq[(nbHdrLen + 1)..];
        var hdrOff = 0;
        byte llRleSym = 0, ofRleSym = 0, mlRleSym = 0;

        if (llMode == 1)
        {
            if (hdrOff >= tableData.Length) throw new InvalidDataException("Truncated LL RLE table");
            llRleSym = tableData[hdrOff++];
        }
        else if (llMode == 2)
        {
            throw new NotSupportedException("FSE-compressed LL table not yet supported");
        }

        if (ofMode == 1)
        {
            if (hdrOff >= tableData.Length) throw new InvalidDataException("Truncated OF RLE table");
            ofRleSym = tableData[hdrOff++];
        }
        else if (ofMode == 2)
        {
            throw new NotSupportedException("FSE-compressed OF table not yet supported");
        }

        if (mlMode == 1)
        {
            if (hdrOff >= tableData.Length) throw new InvalidDataException("Truncated ML RLE table");
            mlRleSym = tableData[hdrOff++];
        }
        else if (mlMode == 2)
        {
            throw new NotSupportedException("FSE-compressed ML table not yet supported");
        }

        var bitstream = tableData[hdrOff..];
        return new SequenceHeader(nbSeq, llMode, ofMode, mlMode, llRleSym, ofRleSym, mlRleSym, tableData, hdrOff, bitstream);
    }

    [StructLayout(LayoutKind.Auto)]
    private ref struct SequenceDecoders(int nbSeq, byte llRle, byte ofRle, byte mlRle)
    {
        public int NbSeq { get; } = nbSeq;
        public bool UseFseLL;
        public bool UseFseOF;
        public bool UseFseML;
        public FseDecoder DecLL;
        public FseDecoder DecOF;
        public FseDecoder DecML;
        public uint StateLL;
        public uint StateOF;
        public uint StateML;
        public int LlLog;
        public int OfLog;
        public int MlLog;
        public byte LlRleSymbol { get; } = llRle;
        public byte OfRleSymbol { get; } = ofRle;
        public byte MlRleSymbol { get; } = mlRle;
        public ReverseBitReader BitReader;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct SequenceCommand(int literalLength, int matchLength, int offset)
    {
        public int LiteralLength { get; } = literalLength;
        public int MatchLength { get; } = matchLength;
        public int Offset { get; } = offset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SequenceDecoders PrepareSequenceDecoders(in SequenceHeader header, ref ZstdDecoderWorkspace.SequenceTableBlock llTables, ref ZstdDecoderWorkspace.SequenceTableBlock mlTables, ref ZstdDecoderWorkspace.SequenceTableBlock ofTables)
    {
        var decoders = new SequenceDecoders(header.NbSeq, header.LlRle, header.OfRle, header.MlRle);

        var llMode = header.LlMode;
        var ofMode = header.OfMode;
        var mlMode = header.MlMode;

        var useFseLL = llMode != 1;
        var useFseOF = ofMode != 1;
        var useFseML = mlMode != 1;

        if (llMode == 0 || ofMode == 0 || mlMode == 0)
        {
            EnsurePredefinedTables();
        }
        if ((llMode == 3 || ofMode == 3 || mlMode == 3) && !hasSeqTables)
        {
            throw new InvalidDataException("Repeat mode without previous tables");
        }

        var tableData = header.TableData;
        var hdrOff = 0;

        var llLog = PrepareSequenceTable(llMode, tableData, ref hdrOff, ZstdLengthsTables.LL_AccuracyLog, ref lastLlLog, maxSymbol: 35, ref llTables, "LL");
        var ofLog = PrepareSequenceTable(ofMode, tableData, ref hdrOff, ZstdLengthsTables.OffsetsAccuracyLog, ref lastOfLog, ZstdLengthsTables.OffsetsMaxN, ref ofTables, "OF");
        var mlLog = PrepareSequenceTable(mlMode, tableData, ref hdrOff, ZstdLengthsTables.ML_AccuracyLog, ref lastMlLog, maxSymbol: 52, ref mlTables, "ML");

        ConfigureDecoders(ref decoders, useFseLL, useFseOF, useFseML, llLog, ofLog, mlLog, ref llTables, ref ofTables, ref mlTables);
        InitializeDecoderStates(ref decoders, header, useFseLL, useFseOF, useFseML, llLog, ofLog, mlLog);
        return decoders;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConfigureDecoders(
        ref SequenceDecoders decoders,
        bool useFseLL,
        bool useFseOF,
        bool useFseML,
        int llLog,
        int ofLog,
        int mlLog,
        ref ZstdDecoderWorkspace.SequenceTableBlock llTables,
        ref ZstdDecoderWorkspace.SequenceTableBlock ofTables,
        ref ZstdDecoderWorkspace.SequenceTableBlock mlTables)
    {
        decoders.UseFseLL = useFseLL;
        decoders.UseFseOF = useFseOF;
        decoders.UseFseML = useFseML;
        decoders.LlLog = llLog;
        decoders.OfLog = ofLog;
        decoders.MlLog = mlLog;

        if (useFseLL)
        {
            EnsureSequenceTableAvailable(llTables, "LL");
            decoders.DecLL = FseDecoder.FromTables(llLog, llTables.GetSymbols(), llTables.GetNbBits(), llTables.GetBase());
        }
        if (useFseOF)
        {
            EnsureSequenceTableAvailable(ofTables, "OF");
            decoders.DecOF = FseDecoder.FromTables(ofLog, ofTables.GetSymbols(), ofTables.GetNbBits(), ofTables.GetBase());
        }
        if (useFseML)
        {
            EnsureSequenceTableAvailable(mlTables, "ML");
            decoders.DecML = FseDecoder.FromTables(mlLog, mlTables.GetSymbols(), mlTables.GetNbBits(), mlTables.GetBase());
        }
    }

    private static void EnsureSequenceTableAvailable(ZstdDecoderWorkspace.SequenceTableBlock tables, string name)
    {
        if (!tables.HasTable)
            throw new InvalidDataException($"{name} table missing for FSE mode");
    }

    private static void InitializeDecoderStates(
        ref SequenceDecoders decoders,
        in SequenceHeader header,
        bool useFseLL,
        bool useFseOF,
        bool useFseML,
        int llLog,
        int ofLog,
        int mlLog)
    {
        var reader = new ReverseBitReader(header.Bitstream);
        if (!reader.TrySkipPadding()) throw new InvalidDataException("Bitstream underflow while skipping padding");

        if (useFseLL && !reader.TryReadBits(llLog, out decoders.StateLL))
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Underflow reading initial LL state (llLog={llLog}, nbSeq={header.NbSeq}, bitstreamLen={header.Bitstream.Length})"));
        if (useFseOF && !reader.TryReadBits(ofLog, out decoders.StateOF))
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Underflow reading initial OF state (ofLog={ofLog}, nbSeq={header.NbSeq}, bitstreamLen={header.Bitstream.Length})"));
        if (useFseML && !reader.TryReadBits(mlLog, out decoders.StateML))
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Underflow reading initial ML state (mlLog={mlLog}, nbSeq={header.NbSeq}, bitstreamLen={header.Bitstream.Length})"));

        decoders.BitReader = reader;
    }

    private int PrepareSequenceTable(
        int mode,
        ReadOnlySpan<byte> tableData,
        ref int offset,
        int defaultLog,
        ref int lastLog,
        int maxSymbol,
        ref ZstdDecoderWorkspace.SequenceTableBlock tables,
        string tableName)
    {
        return mode switch
        {
            0 or 1 => defaultLog,
            2 => BuildSequenceTable(tableData, ref offset, ref tables, maxSymbol, ref lastLog),
            3 => lastLog,
            _ => throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Unsupported {tableName} mode: {mode}")),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int BuildSequenceTable(
        ReadOnlySpan<byte> tableData,
        ref int offset,
        ref ZstdDecoderWorkspace.SequenceTableBlock tables,
        int maxSymbol,
        ref int lastLog)
    {
        Span<short> norm = stackalloc short[maxSymbol + 1];
        var consumed = ParseFseTable(tableData[offset..], maxSymbol, out var log, out var lastSym, norm);
        var normSpan = norm[..(lastSym + 1)];
        tables.GetWritable(log, out var symbols, out var nbBits, out var baseArr);
        FseDecoder.Build(normSpan, log, symbols, nbBits, baseArr);
        tables.Commit(log);
        hasSeqTables = true;
        lastLog = log;
        offset += consumed;
        return log;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EmitLiteralOnlyBlock(in LiteralsInfo literals)
    {
        var pendingSpan = workspace.GetPendingSpan();
        if (literals.Size > pendingSpan.Length) throw new InvalidDataException("Pending buffer overflow");
        var target = pendingSpan[..literals.Size];
        if (literals.IsRle)
        {
            target.Fill(literals.RleValue);
            AppendToWindow(target);
            if (hasChecksum) hash.UpdateRepeat(literals.RleValue, literals.Size);
        }
        else
        {
            var source = GetLiteralData(literals);
            source.CopyTo(target);
            AppendToWindow(source);
            if (hasChecksum) hash.Update(source);
        }

        pendingPos = 0;
        pendingLen = literals.Size;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExecuteSequences(ref SequenceDecoders decoders, in LiteralsInfo literals, in SequenceHeader header)
    {
        var output = workspace.GetPendingSpan();
        var windowSpan = WindowSpan;
        var outPos = 0;
        var literalPosition = 0;
        ref var bitReader = ref decoders.BitReader;

        for (var sequenceIndex = 0; sequenceIndex < decoders.NbSeq; sequenceIndex++)
        {
            var command = DecodeSequence(ref decoders, header, ref bitReader, sequenceIndex);
            outPos = ConsumeLiteralsForSequence(command.LiteralLength, literals, ref literalPosition, output, outPos);
            if (command.MatchLength != 0)
            {
                AppendMatch(windowSpan, output, ref outPos, command.MatchLength, command.Offset);
            }
        }

        AppendRemainingLiterals(literals, output, ref outPos, ref literalPosition);
        pendingPos = 0;
        pendingLen = outPos;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SequenceCommand DecodeSequence(
        ref SequenceDecoders decoders,
        in SequenceHeader header,
        ref ReverseBitReader bitReader,
        int sequenceIndex)
    {
        var llCode = decoders.UseFseLL ? decoders.DecLL.PeekSymbol(decoders.StateLL) : header.LlRle;
        var mlCode = decoders.UseFseML ? decoders.DecML.PeekSymbol(decoders.StateML) : header.MlRle;
        var ofCode = decoders.UseFseOF ? decoders.DecOF.PeekSymbol(decoders.StateOF) : header.OfRle;

        var ofExtra = ReadOptionalBits(ref bitReader, ofCode, "OF extra bits", sequenceIndex, decoders.NbSeq);
        var mlAdd = ZstdLengthsTables.MLAddBits[mlCode];
        var mlExtra = ReadOptionalBits(ref bitReader, mlAdd, "ML extra bits", sequenceIndex, decoders.NbSeq);
        var llAdd = ZstdLengthsTables.LLAddBits[llCode];
        var llExtra = ReadOptionalBits(ref bitReader, llAdd, "LL extra bits", sequenceIndex, decoders.NbSeq);

        UpdateDecoderStates(ref decoders, ref bitReader, sequenceIndex);

        var literalLength = llCode <= 15 ? llCode : (int)(ZstdLengthsTables.LLBase[llCode] + llExtra);
        var matchLength = mlCode <= 31 ? (mlCode + 3) : (int)(ZstdLengthsTables.MLBase[mlCode] + mlExtra);
        var offset = ResolveOffset(literalLength, (1u << ofCode) + ofExtra);

        return new SequenceCommand(literalLength, matchLength, offset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadOptionalBits(
        ref ReverseBitReader bitReader,
        int bitCount,
        string context,
        int sequenceIndex,
        int sequenceCount)
    {
        if (bitCount == 0)
        {
            return 0;
        }

        if (bitReader.TryReadBits(bitCount, out var value))
        {
            return value;
        }

        throw new InvalidDataException(string.Create(
            CultureInfo.InvariantCulture,
            $"Bitstream underflow while reading sequence {sequenceIndex + 1}/{sequenceCount} ({context})"));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ConsumeLiteralsForSequence(
        int literalLength,
        in LiteralsInfo literals,
        ref int literalPosition,
        Span<byte> output,
        int outPos)
    {
        if (literalLength == 0)
        {
            return outPos;
        }

        if (literals.IsRle)
        {
            EnsureOutputCapacity(output, outPos, literalLength);
            var span = output.Slice(outPos, literalLength);
            span.Fill(literals.RleValue);
            AppendToWindow(span);
            if (hasChecksum) hash.UpdateRepeat(literals.RleValue, literalLength);
            return outPos + literalLength;
        }

        var nextLiteralPosition = literalPosition + literalLength;
        if (nextLiteralPosition > literals.Size)
        {
            throw new InvalidDataException(string.Create(
                CultureInfo.InvariantCulture,
                $"LL exceeds literals: litPos={literalPosition}, ll={literalLength}, litRegSize={literals.Size}"));
        }

        var source = GetLiteralSlice(literals, literalPosition, literalLength);
        EnsureOutputCapacity(output, outPos, literalLength);
        source.CopyTo(output.Slice(outPos, literalLength));
        AppendToWindow(source);
        if (hasChecksum) hash.Update(source);
        literalPosition = nextLiteralPosition;
        return outPos + literalLength;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UpdateDecoderStates(ref SequenceDecoders decoders, ref ReverseBitReader br, int sequenceIndex)
    {
        if (decoders.UseFseLL)
        {
            var nbLL = decoders.DecLL.PeekNbBits(decoders.StateLL);
            var addLL = 0u;
            if (nbLL != 0 && !br.TryReadBits(nbLL, out addLL))
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Bitstream underflow while updating LL state (seq={sequenceIndex + 1}/{decoders.NbSeq})"));
            decoders.DecLL.UpdateState(ref decoders.StateLL, addLL);
        }

        if (decoders.UseFseML)
        {
            var nbML = decoders.DecML.PeekNbBits(decoders.StateML);
            var addML = 0u;
            if (nbML != 0 && !br.TryReadBits(nbML, out addML))
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Bitstream underflow while updating ML state (seq={sequenceIndex + 1}/{decoders.NbSeq})"));
            decoders.DecML.UpdateState(ref decoders.StateML, addML);
        }

        if (decoders.UseFseOF)
        {
            var nbOF = decoders.DecOF.PeekNbBits(decoders.StateOF);
            var addOF = 0u;
            if (nbOF != 0 && !br.TryReadBits(nbOF, out addOF))
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Bitstream underflow while updating OF state (seq={sequenceIndex + 1}/{decoders.NbSeq})"));
            decoders.DecOF.UpdateState(ref decoders.StateOF, addOF);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ResolveOffset(int ll, uint offValue)
    {
        if (offValue > 3)
        {
            var offset = (int)(offValue - 3);
            rep3 = rep2;
            rep2 = rep1;
            rep1 = (uint)offset;
            return offset;
        }

        int resolved;
        if (ll == 0)
        {
            if (offValue == 1) { resolved = (int)rep2; rep2 = rep1; rep1 = (uint)resolved; }
            else if (offValue == 2) { resolved = (int)rep3; rep3 = rep2; rep2 = rep1; rep1 = (uint)resolved; }
            else
            {
                resolved = (int)rep1 - 1;
                if (resolved == 0) throw new InvalidDataException("offset=0");
                rep3 = rep2;
                rep2 = rep1;
                rep1 = (uint)resolved;
            }
        }
        else
        {
            if (offValue == 1) { resolved = (int)rep1; }
            else if (offValue == 2)
            {
                resolved = (int)rep2;
                (rep2, rep1) = (rep1, rep2);
            }
            else
            {
                resolved = (int)rep3;
                var temp = rep1;
                rep1 = rep3;
                rep3 = rep2;
                rep2 = temp;
            }
        }

        return resolved;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendMatch(Span<byte> windowSpan, Span<byte> outBuf, ref int outPos, int matchLength, int offset)
    {
        switch (offset)
        {
            case 1:
                AppendRepeatByteMatch(outBuf, ref outPos, matchLength);
                return;
            case 3:
                AppendBufferedMatch(windowSpan, outBuf, ref outPos, matchLength, offset, 3);
                return;
            case 2:
            case 4:
            case 8:
            case 16:
                AppendBufferedMatch(windowSpan, outBuf, ref outPos, matchLength, offset, offset);
                return;
            default:
                AppendGenericMatch(windowSpan, outBuf, ref outPos, matchLength, offset);
                return;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendRepeatByteMatch(Span<byte> outBuf, ref int outPos, int matchLength)
    {
        EnsureOutputCapacity(outBuf, outPos, matchLength);
        var value = GetFromWindow(1);
        var dst = outBuf.Slice(outPos, matchLength);
        dst.Fill(value);
        AppendToWindow(dst);
        if (hasChecksum) hash.UpdateRepeat(value, matchLength);
        outPos += matchLength;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendBufferedMatch(Span<byte> windowSpan, Span<byte> outBuf, ref int outPos, int matchLength, int offset, int initialCopyLength)
    {
        EnsureOutputCapacity(outBuf, outPos, matchLength);
        var dst = outBuf.Slice(outPos, matchLength);
        var baseIdx = winPos - offset;
        if (baseIdx < 0) baseIdx += windowSize;
        var init = Math.Min(initialCopyLength, matchLength);
        for (var k = 0; k < init; k++)
        {
            var idx = baseIdx + k;
            if (idx >= windowSize) idx -= windowSize;
            dst[k] = windowSpan[idx];
        }

        var filled = init;
        while (filled < matchLength)
        {
            var chunk = Math.Min(filled, matchLength - filled);
            dst[..filled].CopyTo(dst.Slice(filled, chunk));
            filled += chunk;
        }

        AppendToWindow(dst);
        if (hasChecksum) hash.Update(dst);
        outPos += matchLength;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendGenericMatch(Span<byte> windowSpan, Span<byte> outBuf, ref int outPos, int matchLength, int offset)
    {
        var remaining = matchLength;
        while (remaining > 0)
        {
            var srcIndex = winPos - offset;
            if (windowSize != 0)
            {
                srcIndex %= windowSize;
                if (srcIndex < 0) srcIndex += windowSize;
            }

            var contiguous = windowSize - srcIndex;
            var toCopy = remaining < offset ? remaining : offset;
            if (toCopy > contiguous) toCopy = contiguous;

            EnsureOutputCapacity(outBuf, outPos, toCopy);
            var dstSpan = outBuf.Slice(outPos, toCopy);
            windowSpan.Slice(srcIndex, toCopy).CopyTo(dstSpan);
            AppendToWindow(dstSpan);
            outPos += toCopy;
            remaining -= toCopy;
        }

        if (hasChecksum) hash.Update(outBuf.Slice(outPos - matchLength, matchLength));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendRemainingLiterals(in LiteralsInfo literals, Span<byte> outBuf, ref int outPos, ref int litPos)
    {
        if (litPos >= literals.Size) return;

        if (!literals.IsRle)
        {
            var tailSpan = GetLiteralData(literals)[litPos..];
            EnsureOutputCapacity(outBuf, outPos, tailSpan.Length);
            tailSpan.CopyTo(outBuf.Slice(outPos, tailSpan.Length));
            AppendToWindow(tailSpan);
            if (hasChecksum) hash.Update(tailSpan);
            outPos += tailSpan.Length;
        }
        else
        {
            var remaining = literals.Size - litPos;
            EnsureOutputCapacity(outBuf, outPos, remaining);
            var dst = outBuf.Slice(outPos, remaining);
            dst.Fill(literals.RleValue);
            AppendToWindow(dst);
            if (hasChecksum) hash.UpdateRepeat(literals.RleValue, remaining);
            outPos += remaining;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DecompressCompressedBlock(int size)
    {
        if (!isWindowInitialized) InitFrameWindow();
        if (hasPendingDict)
        {
            AppendToWindow(pendingDictContent.Span);
            hasPendingDict = false;
        }

        var body = workspace.AcquireCompressed(size);
        if (!ReadExact(body)) throw new InvalidDataException("Truncated compressed block body");
        ref var huffmanBlock = ref workspace.HuffmanTable;

        var literals = DecodeLiteralsSection(body, ref huffmanBlock);
        var seq = body[literals.SectionLength..];
        var header = ParseSequenceHeader(seq);

        if (header.NbSeq == 0)
        {
            EmitLiteralOnlyBlock(literals);
            return;
        }

        ref var llTables = ref LlTables;
        ref var mlTables = ref MlTables;
        ref var ofTables = ref OfTables;
        var decoders = PrepareSequenceDecoders(header, ref llTables, ref mlTables, ref ofTables);

        ExecuteSequences(ref decoders, literals, header);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ParseLiteralsSectionHeader(ReadOnlySpan<byte> src, out int headerLen, out int regeneratedSize, out bool isRle)
    {
        if (src.IsEmpty) throw new InvalidDataException("Empty literals header");
        var b0 = src[0];
        var type = b0 & 0x3;
        var sf = (b0 >> 2) & 0x3;
        if (type is 2 or 3) throw new NotSupportedException("Use ParseCompressedLiteralsHeader for compressed literals");
        isRle = type == 1;
        if (sf is 0 or 2)
        {
            regeneratedSize = b0 >> 3;
            headerLen = 1;
        }
        else if (sf == 1)
        {
            if (src.Length < 2) throw new InvalidDataException("Truncated literals header");
            regeneratedSize = (b0 >> 4) + (src[1] << 4);
            headerLen = 2;
        }
        else
        {
            if (src.Length < 3) throw new InvalidDataException("Truncated literals header");
            regeneratedSize = (b0 >> 4) + (src[1] << 4) + (src[2] << 12);
            headerLen = 3;
        }
    }

    // Compressed/Treeless literals header
    private static void ParseCompressedLiteralsHeader(ReadOnlySpan<byte> src, out int headerLen, out int regeneratedSize, out int compressedSize, out bool isFourStreams)
    {
        var b0 = src[0];
        var sf = (b0 >> 2) & 0x3;
        isFourStreams = sf != 0;
        if (sf == 0)
        {
            // 3 bytes, 10+10 bits (LE)
            if (src.Length < 3) throw new InvalidDataException("Truncated compressed literals header");
            var b1 = src[1];
            var b2 = src[2];
            regeneratedSize = (b0 >> 4) | ((b1 & 0xFC) << 2);
            compressedSize = (b1 & 0x03) | (b2 << 2);
            headerLen = 3;
            return;
        }

        int rBits, cBits, hdr;
        if (sf == 1) { rBits = cBits = 10; hdr = 3; }
        else if (sf == 2) { rBits = cBits = 14; hdr = 4; }
        else { rBits = cBits = 18; hdr = 5; }
        if (src.Length < hdr) throw new InvalidDataException("Truncated compressed literals header");

        var reg = b0 >> 4;
        var cmp = 0;
        var bitPos = 4;
        var bytePos = 1;
        int read;
        while (bitPos < rBits)
        {
            reg |= src[bytePos++] << bitPos;
            bitPos += 8;
        }
        read = 0;
        while (read < cBits)
        {
            cmp |= src[bytePos] << read;
            bytePos++; read += 8;
        }
        regeneratedSize = reg;
        compressedSize = cmp;
        headerLen = hdr;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StoreHuffmanTable() => hasHuffTable = true;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetHuffmanTable(out HuffmanTable table)
    {
        if (!hasHuffTable || !workspace.HuffmanTable.HasTable) { table = default; return false; }
        table = workspace.HuffmanTable.ToTable();
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ParseNbSeq(ReadOnlySpan<byte> src, out int nbSeq, out int headerLen)
    {
        if (src.IsEmpty) throw new InvalidDataException("Empty sequences header");
        var b0 = src[0];
        if (b0 == 0) { nbSeq = 0; headerLen = 1; return; }
        if (b0 < 128) { nbSeq = b0; headerLen = 1; return; }
        if (b0 < 255)
        {
            if (src.Length < 2) throw new InvalidDataException("Truncated nbSeq");
            nbSeq = ((b0 - 0x80) << 8) + src[1]; headerLen = 2; return;
        }
        if (src.Length < 3) throw new InvalidDataException("Truncated nbSeq");
        nbSeq = 0x7F00 + src[1] + (src[2] << 8); headerLen = 3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsurePredefinedTables()
    {
        LlTables.GetWritable(ZstdLengthsTables.LL_AccuracyLog, out var llSym, out var llNb, out var llBase);
        FseDecoder.Build(ZstdLengthsTables.LL_DefaultNorm, ZstdLengthsTables.LL_AccuracyLog,
            llSym, llNb, llBase);
        LlTables.Commit(ZstdLengthsTables.LL_AccuracyLog);

        MlTables.GetWritable(ZstdLengthsTables.ML_AccuracyLog, out var mlSym, out var mlNb, out var mlBase);
        FseDecoder.Build(ZstdLengthsTables.ML_DefaultNorm, ZstdLengthsTables.ML_AccuracyLog,
            mlSym, mlNb, mlBase);
        MlTables.Commit(ZstdLengthsTables.ML_AccuracyLog);

        OfTables.GetWritable(ZstdLengthsTables.OffsetsAccuracyLog, out var ofSym, out var ofNb, out var ofBase);
        FseDecoder.Build(ZstdLengthsTables.OffsetsDefaultNorm, ZstdLengthsTables.OffsetsAccuracyLog,
            ofSym, ofNb, ofBase);
        OfTables.Commit(ZstdLengthsTables.OffsetsAccuracyLog);

        hasSeqTables = true;
        lastLlLog = ZstdLengthsTables.LL_AccuracyLog;
        lastMlLog = ZstdLengthsTables.ML_AccuracyLog;
        lastOfLog = ZstdLengthsTables.OffsetsAccuracyLog;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EnsureOutputCapacity(Span<byte> buffer, int outPos, int toAppend)
    {
        if ((uint)(outPos + toAppend) > (uint)buffer.Length)
            throw new InvalidDataException("Decompressed block exceeds workspace capacity");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DecodeWindowDescriptor(byte wd)
    {
        var exp = wd >> 3;
        var mant = wd & 0x7;
        var baseSize = 1 << (10 + exp);
        var step = baseSize >> 3;
        var size = baseSize + ((long)mant * step);
        if (size < 1024) size = 1024;
        if (size > ZstdStream.MaxWindowSize) size = ZstdStream.MaxWindowSize;
        return (int)size;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InitFrameWindow()
    {
        if (windowSize <= 0) windowSize = 8 * 1024 * 1024;
        _ = workspace.GetWindowSpan(windowSize);
        winPos = 0;
        winFill = 0;
        isWindowInitialized = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendToWindow(ReadOnlySpan<byte> data)
    {
        if (windowSize == 0 || data.IsEmpty) return;
        var windowSpan = WindowSpan;
        var size = windowSize;
        var pos = winPos;
        var len = data.Length;

        var first = Math.Min(len, size - pos);
        data[..first].CopyTo(windowSpan.Slice(pos, first));
        var remaining = len - first;
        if (remaining > 0)
        {
            data.Slice(first, remaining).CopyTo(windowSpan[..remaining]);
        }

        pos += len;
        if (pos >= size) pos -= size;
        winPos = pos;
        var newFill = winFill + len;
        winFill = newFill >= size ? size : newFill;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendOneToWindow(byte b)
    {
        var windowSpan = WindowSpan;
        if (windowSize == 0) return;
        windowSpan[winPos] = b;
        winPos++; if (winPos == windowSize) winPos = 0;
        if (winFill < windowSize) winFill++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendRepeatToWindow(byte b, int count)
    {
        if (windowSize == 0 || count <= 0) return;
        var windowSpan = WindowSpan;
        var size = windowSize;
        var pos = winPos;
        var left = count;
        while (left > 0)
        {
            var chunk = Math.Min(left, size - pos);
            windowSpan.Slice(pos, chunk).Fill(b);
            pos += chunk;
            if (pos == size) pos = 0;
            left -= chunk;
        }
        winPos = pos;
        var newFill = winFill + count;
        winFill = newFill >= size ? size : newFill;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte GetFromWindow(int distance)
    {
        if (windowSize == 0) throw new InvalidOperationException("Window is not initialized");
        var windowSpan = WindowSpan;
        var idx = winPos - distance;
        if (idx < 0) idx += windowSize;
        return windowSpan[idx];
    }

    // ---------- Вспомогательные: чтение FSE-таблиц (NCount) ----------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FloorLog2(int v) => 31 - System.Numerics.BitOperations.LeadingZeroCount((uint)v);

    // Возвращает число байт, потреблённых из src; на выходе tableLog, lastSym (последний индекс символа) и norm[0..lastSym].
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ParseFseTable(ReadOnlySpan<byte> src, int maxSymbol, out int tableLog, out int lastSym, Span<short> norm)
    {
        var reader = new BitReader(src, lsbFirst: true);
        tableLog = (int)reader.ReadBits(4) + 5;
        var remaining = 1 << tableLog;
        var symbol = 0;

        while (remaining > 0 && symbol <= maxSymbol)
        {
            var value = ReadNormalizedCode(ref reader, remaining);
            if (TryHandleSpecialNormalizedValue(ref reader, value, norm, ref symbol, maxSymbol, ref remaining))
            {
                continue;
            }

            var probability = (int)value - 1;
            norm[symbol++] = (short)probability;
            remaining -= probability;
        }

        lastSym = symbol - 1;
        return reader.BytesConsumed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadNormalizedCode(ref BitReader reader, int remaining)
    {
        var range = remaining + 1;
        var bits = FloorLog2(range);
        var threshold = (1 << (bits + 1)) - range;
        var value = reader.ReadBits(bits);
        if (value >= (uint)threshold)
        {
            value = ((value << 1) | reader.ReadBits(1)) - (uint)threshold;
        }

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryHandleSpecialNormalizedValue(
        ref BitReader reader,
        uint value,
        Span<short> norm,
        ref int symbol,
        int maxSymbol,
        ref int remaining)
    {
        switch (value)
        {
            case 0:
                norm[symbol++] = -1;
                remaining -= 1;
                return true;
            case 1:
                norm[symbol++] = 0;
                var zeroRun = ReadZeroRun(ref reader);
                AppendZeroRun(norm, ref symbol, maxSymbol, zeroRun);
                return true;
            default:
                return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReadZeroRun(ref BitReader reader)
    {
        var run = 0;
        while (true)
        {
            var next = (int)reader.ReadBits(2);
            run += next;
            if (next != 3)
            {
                break;
            }
        }

        return run;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendZeroRun(Span<short> norm, ref int symbol, int maxSymbol, int count)
    {
        while (count-- > 0 && symbol <= maxSymbol)
        {
            norm[symbol++] = 0;
        }
    }

    // ---------- Словари ----------
    private void ParseDictionary(ReadOnlyMemory<byte> dictionaryBytes, uint expectedDid)
    {
        if (TryParseFormattedDictionary(dictionaryBytes, expectedDid))
        {
            return;
        }

        ParseRawDictionary(dictionaryBytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryParseFormattedDictionary(ReadOnlyMemory<byte> dictionaryBytes, uint expectedDid)
    {
        var src = dictionaryBytes.Span;
        if (!HasFormattedDictionaryHeader(src))
        {
            return false;
        }

        ValidateDictionaryId(src, expectedDid);
        var position = 8;

        position = ParseDictionaryHuffmanSection(src, position);
        position = ParseDictionarySequenceTables(src, position);
        position = ParseDictionaryRecentOffsets(src, position);

        if (src.Length - position < 8)
        {
            throw new InvalidDataException("Dictionary content too small");
        }

        pendingDictContent = dictionaryBytes[position..];
        hasPendingDict = true;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasFormattedDictionaryHeader(ReadOnlySpan<byte> src)
    {
        if (src.Length < 8)
        {
            return false;
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(src) == 0xEC30A437;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateDictionaryId(ReadOnlySpan<byte> src, uint expectedDid)
    {
        var did = BinaryPrimitives.ReadUInt32LittleEndian(src[4..]);
        if (did != 0 && expectedDid != 0 && did != expectedDid)
        {
            throw new InvalidDataException("DictionaryId mismatch");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ParseDictionaryHuffmanSection(ReadOnlySpan<byte> src, int position)
    {
        if (position >= src.Length)
        {
            throw new InvalidDataException("Truncated dictionary tables");
        }

        var header = src[position];
        position++;

        ref var huffmanBlock = ref workspace.HuffmanTable;
        if (header < 128)
        {
            var weightsSize = header;
            if (position + weightsSize > src.Length)
            {
                throw new InvalidDataException("Truncated dict Huffman weights");
            }

            _ = ZstdWeightsParser.ParseFseWeights(src.Slice(position, weightsSize), ref huffmanBlock, out var consumed);
            if (consumed != weightsSize)
            {
                throw new InvalidDataException("Dict Huffman weights size mismatch");
            }

            StoreHuffmanTable();
            return position + weightsSize;
        }

        var weightCount = header - 127;
        var bytesNeeded = (weightCount + 1) / 2;
        if (position + bytesNeeded > src.Length)
        {
            throw new InvalidDataException("Truncated dict direct weights");
        }

        _ = ZstdWeightsParser.ParseDirectWeights(src.Slice(position, bytesNeeded), weightCount, ref huffmanBlock);
        StoreHuffmanTable();
        return position + bytesNeeded;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ParseDictionarySequenceTables(ReadOnlySpan<byte> src, int position)
    {
        ref var ofTables = ref OfTables;
        ref var mlTables = ref MlTables;
        ref var llTables = ref LlTables;

        position += ParseDictFseTable(src[position..], ZstdLengthsTables.OffsetsMaxN, ref ofTables, out lastOfLog);
        position += ParseDictFseTable(src[position..], 52, ref mlTables, out lastMlLog);
        position += ParseDictFseTable(src[position..], 35, ref llTables, out lastLlLog);
        hasSeqTables = true;
        return position;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ParseDictionaryRecentOffsets(ReadOnlySpan<byte> src, int position)
    {
        if (position + 12 > src.Length)
        {
            throw new InvalidDataException("Truncated dict recent offsets");
        }

        dictRep1 = BinaryPrimitives.ReadUInt32LittleEndian(src[position..]); position += 4;
        dictRep2 = BinaryPrimitives.ReadUInt32LittleEndian(src[position..]); position += 4;
        dictRep3 = BinaryPrimitives.ReadUInt32LittleEndian(src[position..]); position += 4;
        if (dictRep1 == 0 || dictRep2 == 0 || dictRep3 == 0)
        {
            throw new InvalidDataException("Invalid dict recent offsets");
        }

        return position;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ParseRawDictionary(ReadOnlyMemory<byte> dictionaryBytes)
    {
        if (dictionaryBytes.Length < 8)
        {
            throw new InvalidDataException("Raw dictionary too small");
        }

        pendingDictContent = dictionaryBytes;
        hasPendingDict = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ParseDictFseTable(ReadOnlySpan<byte> src, int maxSymbol, ref ZstdDecoderWorkspace.SequenceTableBlock table, out int tableLog)
    {
        Span<short> norm = stackalloc short[maxSymbol + 1];
        var consumed = ParseFseTable(src, maxSymbol, out tableLog, out var lastSym, norm);
        var normSpan = norm[..(lastSym + 1)];
        table.GetWritable(tableLog, out var sym, out var nbBits, out var baseArr);
        FseDecoder.Build(normSpan, tableLog, sym, nbBits, baseArr);
        table.Commit(tableLog);
        return consumed;
    }
}
