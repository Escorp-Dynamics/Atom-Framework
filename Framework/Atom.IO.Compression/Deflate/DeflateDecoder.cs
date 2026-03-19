#pragma warning disable CA2213, MA0051, S3776, CA1857, S109, S907, S1199, S1854, S3626, IDE0004, IDE0055, S1751, MA0076, MA0182, CS0649, S3459, MA0071, IDE0044

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Atom.IO.Compression.Deflate;

/// <summary>
/// Декодер Deflate (RFC 1951). Zero-allocation реализация с единым workspace.
/// </summary>
/// <remarks>
/// Оптимизации:
/// - Единый pinned буфер через DeflateDecoderWorkspace (zero GC pressure)
/// - Прямые указатели без bounds checking
/// - Inline refill в hot loop
/// - Packed Huffman таблицы (symbol + length в одном uint)
/// </remarks>
[SkipLocalsInit]
internal sealed unsafe class DeflateDecoder : IDisposable
{
    #region Constants

    private const int WindowSize = 32768;
    private const int MaxMatchLength = 258;

    #endregion

    #region Fields

    private readonly System.IO.Stream input;
    private readonly DeflateDecoderWorkspace ws;
    private readonly bool ownsWorkspace;

    // Указатели в workspace (кешируем для скорости)
    private readonly byte* inputPtr;
    private readonly byte* unifiedPtr;
    private readonly uint* fixedLitLenPtr;
    private readonly uint* fixedDistPtr;
    private readonly uint* dynLitLenPtr;
    private readonly uint* dynDistPtr;

    // Input buffer state
    private int inputPos;
    private int inputEnd;

    // Unified buffer state
    private int bufferPos;
    private int bufferStart;

    // Bit buffer (64-bit)
    private ulong bitBuffer;
    private int bitsInBuffer;

    // Block state
    private bool isFinalBlock;
    private bool isBlockActive;
    private bool isFirstBlock = true;
    private BlockType blockType;

    // Stored block
    private int storedRemaining;

    // Current Huffman tables (указатели на fixed или dynamic)
    private uint* litLenPtr;
    private int litLenMask;
    private uint* distPtr;
    private int distMask;

    // Dynamic table state — не используем поля, передаём в метод

    // Pending match
    private int pendingLength;
    private int pendingDistance;

    private bool isDisposed;

    #endregion

    #region Constructor

    public DeflateDecoder(System.IO.Stream input, DeflateDecoderWorkspace? workspace = null)
    {
        this.input = input;

        if (workspace != null)
        {
            ws = workspace;
            ownsWorkspace = false;
        }
        else
        {
            ws = DeflateDecoderWorkspacePool.Rent();
            ownsWorkspace = true;
        }

        // Кешируем указатели
        inputPtr = ws.InputPtr;
        unifiedPtr = ws.UnifiedPtr;
        fixedLitLenPtr = ws.FixedLitLenPtr;
        fixedDistPtr = ws.FixedDistPtr;
        dynLitLenPtr = ws.DynLitLenPtr;
        dynDistPtr = ws.DynDistPtr;

        // Начинаем с позиции WindowSize (первые 32KB = пустая история)
        bufferPos = WindowSize;
        bufferStart = WindowSize;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Копирует все декодированные данные напрямую в целевой поток.
    /// Минует промежуточный буфер Read, экономя одну memcpy на каждый batch.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void CopyToStream(System.IO.Stream destination)
    {
        // Pre-size MemoryStream to avoid resize cascade
        if (destination is System.IO.MemoryStream ms && ms.Position == 0 && ms.Length == 0)
        {
            var needed = ws.UnifiedCapacity - WindowSize;
            if (ms.Capacity < needed)
                ms.Capacity = needed;
        }

        while (true)
        {
            // 1. Записываем готовые данные напрямую в destination (без промежуточного буфера)
            var available = bufferPos - bufferStart;
            if (available > 0)
            {
                destination.Write(new ReadOnlySpan<byte>(unifiedPtr + bufferStart, available));
                bufferStart += available;
            }

            // 2. Slide window если буфер почти полон
            if (bufferPos >= ws.UnifiedCapacity - MaxMatchLength)
            {
                SlideWindow();
            }

            // 3. Обработка pending match
            if (pendingLength > 0)
            {
                var space = ws.UnifiedCapacity - bufferPos;
                var toCopy = Math.Min(pendingLength, space);
                CopyMatch(pendingDistance, toCopy);
                pendingLength -= toCopy;
                continue;
            }

            // 4. Читаем заголовок блока если нужно
            if (!isBlockActive)
            {
                if (isFinalBlock && !isFirstBlock)
                {
                    // Записываем остаток перед выходом
                    var remaining = bufferPos - bufferStart;
                    if (remaining > 0)
                        destination.Write(new ReadOnlySpan<byte>(unifiedPtr + bufferStart, remaining));
                    return;
                }

                if (!ReadBlockHeader())
                {
                    var remaining = bufferPos - bufferStart;
                    if (remaining > 0)
                        destination.Write(new ReadOnlySpan<byte>(unifiedPtr + bufferStart, remaining));
                    return;
                }
                isFirstBlock = false;
            }

            // 5. Декодируем данные блока
            var decoded = DecodeBlock();
            if (decoded == 0)
            {
                var remaining = bufferPos - bufferStart;
                if (remaining > 0)
                    destination.Write(new ReadOnlySpan<byte>(unifiedPtr + bufferStart, remaining));
                return;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty) return 0;

        fixed (byte* outPtr = buffer)
        {
            return ReadCore(outPtr, buffer.Length);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private int ReadCore(byte* output, int length)
    {
        var written = 0;

        while (written < length)
        {
            // 1. Копируем готовые данные из unified buffer
            var available = bufferPos - bufferStart;
            if (available > 0)
            {
                var toCopy = Math.Min(available, length - written);
                Buffer.MemoryCopy(unifiedPtr + bufferStart, output + written, toCopy, toCopy);
                bufferStart += toCopy;
                written += toCopy;

                if (written >= length)
                    return written;
            }

            // 2. Slide window если буфер почти полон
            if (bufferPos >= ws.UnifiedCapacity - MaxMatchLength)
            {
                SlideWindow();
            }

            // 3. Обработка pending match
            if (pendingLength > 0)
            {
                var space = ws.UnifiedCapacity - bufferPos;
                var toCopy = Math.Min(pendingLength, space);
                CopyMatch(pendingDistance, toCopy);
                pendingLength -= toCopy;
                continue;
            }

            // 4. Читаем заголовок блока если нужно
            if (!isBlockActive)
            {
                // isFinalBlock проверяем только когда isBlockActive стало false
                // после полной обработки предыдущего блока
                if (isFinalBlock && !isFirstBlock)
                {
                    return written;
                }

                if (!ReadBlockHeader())
                {
                    return written;
                }
                isFirstBlock = false;
            }

            // 5. Декодируем данные блока
            var decoded = DecodeBlock();
            if (decoded == 0)
            {
                return written;
            }
        }

        return written;
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken _ = default) =>
        ValueTask.FromResult(Read(buffer.Span));

    #endregion

    #region Window Management

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SlideWindow()
    {
        // Копируем последние 32KB в начало
        Buffer.MemoryCopy(unifiedPtr + bufferPos - WindowSize, unifiedPtr, WindowSize, WindowSize);
        bufferPos = WindowSize;
        bufferStart = WindowSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CopyMatch(int distance, int length)
    {
        var dst = unifiedPtr + bufferPos;
        var src = dst - distance;

        if (distance >= 8)
        {
            // Non-overlapping: fast 8-byte copy
            var rem = length;
            while (rem >= 8)
            {
                *(ulong*)dst = *(ulong*)src;
                dst += 8; src += 8; rem -= 8;
            }
            if (rem > 0)
                *(ulong*)dst = *(ulong*)src; // Copy remaining (may overlap with next)
        }
        else if (distance == 1)
        {
            // RLE: repeat single byte
            var b = *src;
            var fill = (ulong)b * 0x0101010101010101UL;
            var rem = length;
            while (rem >= 8) { *(ulong*)dst = fill; dst += 8; rem -= 8; }
            while (rem-- > 0) *dst++ = b;
        }
        else
        {
            // Overlapping copy
            for (var i = 0; i < length; i++)
                dst[i] = src[i];
        }

        bufferPos += length;
    }

    #endregion

    #region Block Header

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ReadBlockHeader()
    {
        if (!EnsureBits(3)) return false;

        isFinalBlock = (bitBuffer & 1) != 0;
        blockType = (BlockType)((bitBuffer >> 1) & 3);
        ConsumeBits(3);
        isBlockActive = true;

        return blockType switch
        {
            BlockType.Stored => InitStoredBlock(),
            BlockType.FixedHuffman => InitFixedBlock(),
            BlockType.DynamicHuffman => InitDynamicBlock(),
            _ => throw new InvalidDataException("Invalid BTYPE"),
        };
    }

    private bool InitStoredBlock()
    {
        // Выравнивание на байт
        var skip = bitsInBuffer & 7;
        if (skip > 0) ConsumeBits(skip);

        if (!EnsureBits(32)) return false;

        var len = (int)(bitBuffer & 0xFFFF);
        var nlen = (int)((bitBuffer >> 16) & 0xFFFF);
        ConsumeBits(32);

        if ((len ^ nlen) != 0xFFFF)
            throw new InvalidDataException("Invalid stored block length");

        storedRemaining = len;
        return true;
    }

    private bool InitFixedBlock()
    {
        litLenPtr = fixedLitLenPtr;
        litLenMask = ws.FixedLitLenMask;
        distPtr = fixedDistPtr;
        distMask = ws.FixedDistMask;
        return true;
    }

    private bool InitDynamicBlock()
    {
        if (!EnsureBits(14)) return false;

        var hlit = (int)(bitBuffer & 0x1F) + 257;
        var hdist = (int)((bitBuffer >> 5) & 0x1F) + 1;
        var hclen = (int)((bitBuffer >> 10) & 0xF) + 4;
        ConsumeBits(14);

        // Читаем code length code lengths
        Span<byte> clCodeLengths = stackalloc byte[19];
        clCodeLengths.Clear(); // ВАЖНО: инициализируем нулями!
        ReadOnlySpan<int> clOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];

        for (var i = 0; i < hclen; i++)
        {
            if (!EnsureBits(3)) return false;
            clCodeLengths[clOrder[i]] = (byte)(bitBuffer & 7);
            ConsumeBits(3);
        }

        // Строим таблицу code lengths
        Span<uint> clTable = stackalloc uint[128]; // 7-bit max
        var clMask = BuildHuffmanTable(clCodeLengths, 19, clTable, 7);

        // Читаем lit/len и distance code lengths
        var totalCodes = hlit + hdist;
        var codeLengths = ws.CodeLengthsPtr;

        // Очищаем буфер — важно для переиспользования workspace
        new Span<byte>(codeLengths, totalCodes).Clear();

        var idx = 0;
        while (idx < totalCodes)
        {
            if (!EnsureBits(15)) return false;

            var entry = clTable[(int)(bitBuffer & (uint)clMask)];
            var len = (int)(entry & 0xFF);
            var sym = (int)(entry >> 8);
            ConsumeBits(len);

            if (sym < 16)
            {
                codeLengths[idx++] = (byte)sym;
            }
            else if (sym == 16)
            {
                if (!EnsureBits(2)) return false;
                var repeat = (int)(bitBuffer & 3) + 3;
                ConsumeBits(2);
                var prev = idx > 0 ? codeLengths[idx - 1] : (byte)0;
                for (var r = 0; r < repeat && idx < totalCodes; r++)
                    codeLengths[idx++] = prev;
            }
            else if (sym == 17)
            {
                if (!EnsureBits(3)) return false;
                var repeat = (int)(bitBuffer & 7) + 3;
                ConsumeBits(3);
                for (var r = 0; r < repeat && idx < totalCodes; r++)
                    codeLengths[idx++] = 0;
            }
            else // sym == 18
            {
                if (!EnsureBits(7)) return false;
                var repeat = (int)(bitBuffer & 0x7F) + 11;
                ConsumeBits(7);
                for (var r = 0; r < repeat && idx < totalCodes; r++)
                    codeLengths[idx++] = 0;
            }
        }

        // Строим таблицы lit/len и distance
        var litLenLengths = new ReadOnlySpan<byte>(codeLengths, hlit);
        litLenMask = BuildHuffmanTablePtr(litLenLengths, hlit, dynLitLenPtr, DeflateDecoderWorkspace.PrimaryBits, 286, 256);
        litLenPtr = dynLitLenPtr;

        var distLengths = new ReadOnlySpan<byte>(codeLengths + hlit, hdist);
        distMask = BuildHuffmanTablePtr(distLengths, hdist, dynDistPtr, DeflateDecoderWorkspace.PrimaryBits, 30, 0);
        distPtr = dynDistPtr;

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int BuildHuffmanTable(ReadOnlySpan<byte> codeLengths, int numSymbols, Span<uint> table, int tableLog)
    {
        var tableSize = 1 << tableLog;
        var mask = tableSize - 1;

        var defaultEntry = (0u << 8) | (uint)tableLog;
        table[..tableSize].Fill(defaultEntry);

        var actualSymbols = Math.Min(numSymbols, codeLengths.Length);

        Span<int> blCount = stackalloc int[16];
        blCount.Clear();
        for (var i = 0; i < actualSymbols; i++)
        {
            var cl = codeLengths[i];
            if (cl is > 0 and <= 15)
                blCount[cl]++;
        }

        Span<int> nextCode = stackalloc int[16];
        var code = 0;
        for (var bits = 1; bits <= 15; bits++)
        {
            code = (code + blCount[bits - 1]) << 1;
            nextCode[bits] = code;
        }

        ref var bitRev = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(DeflateTables.BitReverse8);

        for (var sym = 0; sym < actualSymbols; sym++)
        {
            var len = codeLengths[sym];
            if (len is 0 or > 15) continue;

            var huffCode = nextCode[len]++;
            var fillBits = tableLog - len;
            if (fillBits < 0) continue;

            var reversed = (Unsafe.Add(ref bitRev, huffCode & 0xFF) << 8)
                         | Unsafe.Add(ref bitRev, (huffCode >> 8) & 0xFF);
            var baseIndex = reversed >> (16 - len);

            var entry = (uint)((sym << 8) | len);
            var fillCount = 1 << fillBits;
            for (var fill = 0; fill < fillCount; fill++)
                table[baseIndex | (fill << len)] = entry;
        }

        return mask;
    }

    /// <summary>
    /// Строит двухуровневую Huffman таблицу.
    /// Primary: (1 &lt;&lt; primaryBits) записей. Коды &lt;= primaryBits → прямой entry (sym &lt;&lt; 8 | len).
    /// Коды &gt; primaryBits → redirect entry (subtableOffset &lt;&lt; 8 | subtableBits | 0x40).
    /// Secondary subtables размещаются после primary в том же буфере.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int BuildHuffmanTablePtr(ReadOnlySpan<byte> codeLengths, int numSymbols, uint* table, int primaryBits, int maxValidSymbol = int.MaxValue, int defaultSymbol = 0)
    {
        if (primaryBits == 0)
            primaryBits = 1;

        var primarySize = 1 << primaryBits;
        var primaryMask = primarySize - 1;

        var actualSymbols = Math.Min(numSymbols, codeLengths.Length);

        // Подсчёт кодов по длинам
        Span<int> blCount = stackalloc int[16];
        blCount.Clear();
        ref var clRef = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(codeLengths);
        var maxLen = 0;
        for (var i = 0; i < actualSymbols; i++)
        {
            var cl = Unsafe.Add(ref clRef, i);
            if (cl is > 0 and <= 15)
            {
                blCount[cl]++;
                if (cl > maxLen) maxLen = cl;
            }
        }

        // Если все коды <= primaryBits — flat таблица (быстрый путь)
        if (maxLen <= primaryBits)
        {
            var defaultEntry = ((uint)defaultSymbol << 8) | (uint)(maxLen > 0 ? maxLen : primaryBits);
            var defaultEntry64 = defaultEntry | ((ulong)defaultEntry << 32);
            var tableLongs = primarySize >> 1;
            var tablePtr64 = (ulong*)table;
            for (var i = 0; i < tableLongs; i++)
                tablePtr64[i] = defaultEntry64;

            Span<int> nextCode = stackalloc int[16];
            var code = 0;
            for (var bits = 1; bits <= 15; bits++)
            {
                code = (code + blCount[bits - 1]) << 1;
                nextCode[bits] = code;
            }

            ref var bitRev = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(DeflateTables.BitReverse8);

            for (var sym = 0; sym < actualSymbols; sym++)
            {
                var len = Unsafe.Add(ref clRef, sym);
                if (len is 0 or > 15) continue;

                var huffCode = nextCode[len]++;
                if (sym >= maxValidSymbol) continue;

                var reversed = (Unsafe.Add(ref bitRev, huffCode & 0xFF) << 8)
                             | Unsafe.Add(ref bitRev, (huffCode >> 8) & 0xFF);
                var baseIndex = reversed >> (16 - len);

                var entry = (uint)((sym << 8) | len);
                var fillCount = 1 << (primaryBits - len);

                if (fillCount == 1)
                {
                    table[baseIndex] = entry;
                }
                else if (fillCount == 2)
                {
                    table[baseIndex] = entry;
                    table[baseIndex | (1 << len)] = entry;
                }
                else
                {
                    for (var fill = 0; fill < fillCount; fill++)
                        table[baseIndex | (fill << len)] = entry;
                }
            }

            return primaryMask;
        }

        // Двухуровневая таблица: maxLen > primaryBits
        {
            Span<int> nextCode = stackalloc int[16];
            var code = 0;
            for (var bits = 1; bits <= 15; bits++)
            {
                code = (code + blCount[bits - 1]) << 1;
                nextCode[bits] = code;
            }

            ref var bitRev = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(DeflateTables.BitReverse8);

            var defaultEntry = ((uint)defaultSymbol << 8) | (uint)primaryBits;
            var defaultEntry64 = defaultEntry | ((ulong)defaultEntry << 32);
            var tableLongs = primarySize >> 1;
            var tablePtr64 = (ulong*)table;
            for (var i = 0; i < tableLongs; i++)
                tablePtr64[i] = defaultEntry64;

            Span<int> subtableMaxLen = stackalloc int[primarySize];
            subtableMaxLen.Clear();

            Span<int> nextCode2 = stackalloc int[16];
            nextCode.CopyTo(nextCode2);

            for (var sym = 0; sym < actualSymbols; sym++)
            {
                var len = (int)Unsafe.Add(ref clRef, sym);
                if (len is 0 or > 15) continue;

                var huffCode = nextCode2[len]++;
                if (sym >= maxValidSymbol) continue;

                var reversed = (Unsafe.Add(ref bitRev, huffCode & 0xFF) << 8)
                             | Unsafe.Add(ref bitRev, (huffCode >> 8) & 0xFF);
                var baseIndex = reversed >> (16 - len);

                if (len <= primaryBits)
                {
                    var entry = (uint)((sym << 8) | len);
                    var fillCount = 1 << (primaryBits - len);
                    for (var fill = 0; fill < fillCount; fill++)
                        table[baseIndex | (fill << len)] = entry;
                }
                else
                {
                    var primaryIndex = baseIndex & primaryMask;
                    var secondaryLen = len - primaryBits;
                    if (secondaryLen > subtableMaxLen[primaryIndex])
                        subtableMaxLen[primaryIndex] = secondaryLen;
                }
            }

            var secondaryStart = primarySize;
            Span<int> subtableOffset = stackalloc int[primarySize];

            for (var pi = 0; pi < primarySize; pi++)
            {
                if (subtableMaxLen[pi] > 0)
                {
                    var subBits = subtableMaxLen[pi];
                    var subSize = 1 << subBits;

                    subtableOffset[pi] = secondaryStart;

                    // Redirect entry в primary: offset | subBits | 0x40
                    table[pi] = (uint)((secondaryStart << 8) | subBits | 0x40);

                    var subDefault = ((uint)defaultSymbol << 8) | (uint)subBits;
                    for (var si = 0; si < subSize; si++)
                        table[secondaryStart + si] = subDefault;

                    secondaryStart += subSize;
                }
            }

            // Проход 2: заполняем secondary entries для длинных кодов
            code = 0;
            for (var bits = 1; bits <= 15; bits++)
            {
                code = (code + blCount[bits - 1]) << 1;
                nextCode[bits] = code;
            }

            for (var sym = 0; sym < actualSymbols; sym++)
            {
                var len = (int)Unsafe.Add(ref clRef, sym);
                if (len is 0 or > 15) continue;

                var huffCode = nextCode[len]++;
                if (sym >= maxValidSymbol) continue;

                if (len <= primaryBits) continue;

                var reversed = (Unsafe.Add(ref bitRev, huffCode & 0xFF) << 8)
                             | Unsafe.Add(ref bitRev, (huffCode >> 8) & 0xFF);
                var baseIndex = reversed >> (16 - len);

                var primaryIndex = baseIndex & primaryMask;
                var secondaryBits = baseIndex >> primaryBits;
                var subBits = subtableMaxLen[primaryIndex];
                var subStart = subtableOffset[primaryIndex];

                var secondaryLen = len - primaryBits;
                var entry = (uint)((sym << 8) | secondaryLen);
                var fillCount = 1 << (subBits - secondaryLen);

                for (var fill = 0; fill < fillCount; fill++)
                    table[subStart + (secondaryBits | (fill << secondaryLen))] = entry;
            }
        }

        return primaryMask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReverseBits(int value, int numBits) =>
        DeflateTables.ReverseBits(value, numBits);

    #endregion

    #region Block Decoding

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private int DecodeBlock()
    {
        return blockType switch
        {
            BlockType.Stored => DecodeStoredBlock(),
            BlockType.FixedHuffman => DecodeFixedHuffmanBlock(),
            BlockType.DynamicHuffman => DecodeHuffmanBlock(),
            _ => 0,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private int DecodeStoredBlock()
    {
        var startPos = bufferPos;
        var bufEnd = ws.UnifiedCapacity;

        // Сначала извлекаем байты из bit buffer
        while (storedRemaining > 0 && bitsInBuffer >= 8 && bufferPos < bufEnd)
        {
            unifiedPtr[bufferPos++] = (byte)bitBuffer;
            ConsumeBits(8);
            storedRemaining--;
        }

        // Затем копируем напрямую из input buffer
        while (storedRemaining > 0 && bufferPos < bufEnd)
        {
            if (inputPos >= inputEnd)
            {
                inputEnd = input.Read(new Span<byte>(inputPtr, ws.InputCapacity));
                inputPos = 0;
                if (inputEnd == 0) break;
            }

            var canRead = Math.Min(storedRemaining, inputEnd - inputPos);
            var canWrite = bufEnd - bufferPos;
            var toCopy = Math.Min(canRead, canWrite);

            Buffer.MemoryCopy(inputPtr + inputPos, unifiedPtr + bufferPos, toCopy, toCopy);
            inputPos += toCopy;
            bufferPos += toCopy;
            storedRemaining -= toCopy;
        }

        if (storedRemaining == 0)
            isBlockActive = false;

        return bufferPos - startPos;
    }

    // Fixed Huffman: 3-level loop (outer → refill → inner).
    // After refill to ≥56 bits: max consumption per match = 9(lit/len) + 5(extraLen) + 5(dist) + 13(extraDist) = 32.
    // 56 ≥ 32, so no mid-match refill needed. Inner loop runs until bitsCount < 9 (min lookup size).
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private int DecodeFixedHuffmanBlock()
    {
        unchecked
        {
            var startPos = bufferPos;
            var bufEnd = ws.UnifiedCapacity - MaxMatchLength;
            var buf = unifiedPtr;
            var inp = inputPtr;
            var ll = litLenPtr;
            const uint llMask = DeflateDecoderWorkspace.PrimarySize - 1;

            var pos = bufferPos;
            var bits = bitBuffer;
            var bitsCount = bitsInBuffer;
            var inPos = inputPos;
            var inEnd = inputEnd;

            while (pos < bufEnd)
            {
                var inFastLimit = inEnd - 8;
                if (inPos <= inFastLimit)
                {
                    // ── FAST PATH: 3-level loop (outer → refill → inner) ──
                    while (true)
                    {
                        // Refill to ≥56 bits
                        if (bitsCount < 56)
                        {
                            if (inPos <= inFastLimit)
                            {
                                bits |= *(ulong*)(inp + inPos) << bitsCount;
                                inPos += (63 - bitsCount) >> 3;
                                bitsCount |= 56;
                            }
                            else { break; }
                        }

                        // ── Inner loop: decode symbols without refill ──
                        do
                        {
                            var entry = ll[bits & llMask];
                            var len = (int)(entry & 0xFF);
                            var sym = (int)(entry >> 8);
                            bits >>= len; bitsCount -= len;

                            if (sym >= 256)
                            {
                                if (sym == 256)
                                    goto FixedSave256;

                                // ── Match decode ──
                                // Need up to 5(extraLen)+5(dist)+13(extraDist) = 23 bits.
                                if (bitsCount < 24)
                                {
                                    if (inPos <= inFastLimit)
                                    {
                                        bits |= *(ulong*)(inp + inPos) << bitsCount;
                                        inPos += (63 - bitsCount) >> 3;
                                        bitsCount |= 56;
                                    }
                                    else
                                    {
                                        while (bitsCount <= 56 && inPos < inEnd)
                                        { bits |= (ulong)inp[inPos++] << bitsCount; bitsCount += 8; }
                                    }
                                }

                                var lengthCode = sym - 257;
                                if ((uint)lengthCode > 28u)
                                    goto FixedInvalidData;

                                ref var lengthPacked = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(DeflateTables.LengthPacked);
                                var lp = Unsafe.Add(ref lengthPacked, lengthCode);
                                var matchLen = (int)(lp & 0xFFFF) + (int)(bits & ((1u << (int)(lp >> 16)) - 1));
                                bits >>= (int)(lp >> 16);
                                bitsCount -= (int)(lp >> 16);

                                var ds = distPtr;
                                ref var distancePacked = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(DeflateTables.DistancePacked);

                                var distCode = (int)(ds[bits & 31u] >> 8);
                                bits >>= 5; bitsCount -= 5;

                                if ((uint)distCode > 29u)
                                    goto FixedInvalidData;

                                var dp = Unsafe.Add(ref distancePacked, distCode);
                                var distance = (int)(dp & 0xFFFF) + (int)(bits & ((1u << (int)(dp >> 16)) - 1));
                                bits >>= (int)(dp >> 16);
                                bitsCount -= (int)(dp >> 16);

                                // Copy match
                                var src = buf + pos - distance;
                                var dst = buf + pos;
                                pos += matchLen;

                                if (distance >= 16)
                                {
                                    Vector128.LoadUnsafe(ref *src).StoreUnsafe(ref *dst);
                                    if (matchLen > 16)
                                    {
                                        var mEnd = dst + matchLen;
                                        dst += 16; src += 16;
                                        do { Vector128.LoadUnsafe(ref *src).StoreUnsafe(ref *dst); dst += 16; src += 16; } while (dst < mEnd);
                                    }
                                }
                                else if (distance >= 8)
                                {
                                    *(ulong*)dst = *(ulong*)src;
                                    if (matchLen > 8)
                                    {
                                        *(ulong*)(dst + 8) = *(ulong*)(src + 8);
                                        if (matchLen > 16)
                                        {
                                            var mEnd = dst + matchLen;
                                            dst += 16; src += 16;
                                            do { *(ulong*)dst = *(ulong*)src; dst += 8; src += 8; } while (dst < mEnd);
                                        }
                                    }
                                }
                                else if (distance == 1)
                                {
                                    Unsafe.InitBlockUnaligned(dst, *src, (uint)matchLen);
                                }
                                else
                                {
                                    ulong pat;
                                    if (distance == 4)
                                    {
                                        pat = *(uint*)src;
                                        pat |= pat << 32;
                                    }
                                    else if (distance == 2)
                                    {
                                        pat = *(ushort*)src;
                                        pat |= pat << 16;
                                        pat |= pat << 32;
                                    }
                                    else
                                    {
                                        pat = (ulong)src[0] | ((ulong)src[1] << 8);
                                        pat |= (ulong)src[2 % distance] << 16;
                                        pat |= (ulong)src[3 % distance] << 24;
                                        pat |= (ulong)src[4 % distance] << 32;
                                        pat |= (ulong)src[5 % distance] << 40;
                                        pat |= (ulong)src[6 % distance] << 48;
                                        pat |= (ulong)src[7 % distance] << 56;
                                    }
                                    *(ulong*)dst = pat;
                                    if (matchLen > 8)
                                    {
                                        var mEnd = dst + matchLen;
                                        dst += 8;
                                        do { *(ulong*)dst = pat; dst += 8; } while (dst < mEnd);
                                    }
                                }

                                // After match, break to outer for refill
                                break;
                            }

                            buf[pos++] = (byte)sym;
                        }
                        while (bitsCount >= 9 && pos < bufEnd);

                        if (pos >= bufEnd)
                            goto FixedSave;
                    }

                    if (pos >= bufEnd)
                        goto FixedSave;

                    // Clear dirty bits above bitsCount before entering slow path
                    if (bitsCount < 64)
                        bits &= (1UL << bitsCount) - 1;
                }

                // --- SLOW PATH: refill from stream ---
                if (inPos >= inEnd && bitsCount < 9)
                {
                    bufferPos = pos;
                    inputPos = inPos;
                    bitBuffer = bits;
                    bitsInBuffer = bitsCount;
                    RefillInput();

                    bits = bitBuffer;
                    bitsCount = bitsInBuffer;
                    inPos = inputPos;
                    inEnd = inputEnd;

                    if (inEnd == 0 && bitsCount == 0)
                        return pos - startPos;

                    if (inEnd > 0)
                        continue;
                }

                // Byte-by-byte refill + decode one symbol
                while (bitsCount <= 56 && inPos < inEnd)
                {
                    bits |= (ulong)inp[inPos++] << bitsCount;
                    bitsCount += 8;
                }

                var entryS = ll[bits & llMask];
                var lenS = (int)(entryS & 0xFF);
                var symS = (int)(entryS >> 8);
                bits >>= lenS; bitsCount -= lenS;

                if (symS < 256)
                {
                    buf[pos++] = (byte)symS;
                    continue;
                }

                if (symS == 256)
                {
                    bufferPos = pos;
                    inputPos = inPos;
                    bitBuffer = bits;
                    bitsInBuffer = bitsCount;
                    isBlockActive = false;
                    return pos - startPos;
                }

                // Match in slow path
                var lengthCodeS = symS - 257;
                if ((uint)lengthCodeS > 28u) goto FixedInvalidData;

                ref var lengthPackedS = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(DeflateTables.LengthPacked);
                var lpS = Unsafe.Add(ref lengthPackedS, lengthCodeS);
                var matchLenS = (int)(lpS & 0xFFFF);
                var extraLenS = (int)(lpS >> 16);

                {
                    // Need extraLen + 5(dist) + 13(extraDist) = up to 23 bits
                    var needed = extraLenS + 5 + 13;
                    if (bitsCount < needed)
                    {
                        while (bitsCount <= 56 && inPos < inEnd)
                        { bits |= (ulong)inp[inPos++] << bitsCount; bitsCount += 8; }

                        if (bitsCount < needed && inPos >= inEnd)
                        {
                            bufferPos = pos; inputPos = inPos; bitBuffer = bits; bitsInBuffer = bitsCount;
                            RefillInput();
                            bits = bitBuffer; bitsCount = bitsInBuffer; inPos = inputPos; inEnd = inputEnd;
                            while (bitsCount <= 56 && inPos < inEnd)
                            { bits |= (ulong)inp[inPos++] << bitsCount; bitsCount += 8; }
                        }
                    }
                }

                if (extraLenS != 0)
                {
                    matchLenS += (int)(bits & ((1u << extraLenS) - 1));
                    bits >>= extraLenS; bitsCount -= extraLenS;
                }

                var dsS = distPtr;
                ref var distancePackedS = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(DeflateTables.DistancePacked);

                var distCodeS = (int)(dsS[bits & 31u] >> 8);
                bits >>= 5; bitsCount -= 5;

                if ((uint)distCodeS > 29u) goto FixedInvalidData;

                var dpS = Unsafe.Add(ref distancePackedS, distCodeS);
                var distanceS = (int)(dpS & 0xFFFF);
                var extraDistS = (int)(dpS >> 16);
                if (extraDistS != 0)
                {
                    distanceS += (int)(bits & ((1u << extraDistS) - 1));
                    bits >>= extraDistS; bitsCount -= extraDistS;
                }

                var srcS = buf + pos - distanceS;
                var dstS = buf + pos;
                pos += matchLenS;

                if (distanceS >= 8)
                {
                    while (matchLenS >= 8) { *(ulong*)dstS = *(ulong*)srcS; dstS += 8; srcS += 8; matchLenS -= 8; }
                    if (matchLenS > 0) *(ulong*)dstS = *(ulong*)srcS;
                }
                else if (distanceS == 1)
                {
                    var fillS = (ulong)*srcS * 0x0101010101010101UL;
                    while (matchLenS >= 8) { *(ulong*)dstS = fillS; dstS += 8; matchLenS -= 8; }
                    while (matchLenS-- > 0) *dstS++ = *srcS;
                }
                else
                {
                    ulong patS;
                    if (distanceS == 4) { patS = *(uint*)srcS; patS |= patS << 32; }
                    else if (distanceS == 2) { patS = *(ushort*)srcS; patS |= patS << 16; patS |= patS << 32; }
                    else
                    {
                        patS = (ulong)srcS[0] | ((ulong)srcS[1] << 8);
                        patS |= (ulong)srcS[2 % distanceS] << 16;
                        patS |= (ulong)srcS[3 % distanceS] << 24;
                        patS |= (ulong)srcS[4 % distanceS] << 32;
                        patS |= (ulong)srcS[5 % distanceS] << 40;
                        patS |= (ulong)srcS[6 % distanceS] << 48;
                        patS |= (ulong)srcS[7 % distanceS] << 56;
                    }
                    *(ulong*)dstS = patS;
                    if (matchLenS > 8)
                    {
                        var mEndS = dstS + matchLenS;
                        dstS += 8;
                        do { *(ulong*)dstS = patS; dstS += 8; } while (dstS < mEndS);
                    }
                }
            }

        FixedSave:
            if (bitsCount < 64)
                bits &= (1UL << bitsCount) - 1;
            bufferPos = pos;
            inputPos = inPos;
            bitBuffer = bits;
            bitsInBuffer = bitsCount;
            return pos - startPos;

        FixedSave256:
            if (bitsCount < 64)
                bits &= (1UL << bitsCount) - 1;
            bufferPos = pos;
            inputPos = inPos;
            bitBuffer = bits;
            bitsInBuffer = bitsCount;
            isBlockActive = false;
            return pos - startPos;

        FixedInvalidData:
            bufferPos = pos;
            inputPos = inPos;
            bitBuffer = bits;
            bitsInBuffer = bitsCount;
            throw new InvalidDataException("Invalid Deflate data");
        }
    }

    // Dynamic Huffman: 3-level loop (outer → refill → inner).
    // After refill to ≥56 bits: max consumption per match = 15(lit/len) + 5(extraLen) + 15(dist) + 13(extraDist) = 48.
    // 56 ≥ 48, so no mid-match refill needed. Inner loop runs until bitsCount < 15 (min code len).
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private int DecodeHuffmanBlock()
    {
        unchecked
        {
            var startPos = bufferPos;
            var bufEnd = ws.UnifiedCapacity - MaxMatchLength;
            var buf = unifiedPtr;
            var inp = inputPtr;
            var ll = litLenPtr;
            var ds = distPtr;
            var llMask = (uint)litLenMask;
            var dsMask = (uint)distMask;

            var pos = bufferPos;
            var bits = bitBuffer;
            var bitsCount = bitsInBuffer;
            var inPos = inputPos;
            var inEnd = inputEnd;

            ref var lengthPacked = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(DeflateTables.LengthPacked);
            ref var distancePacked = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(DeflateTables.DistancePacked);

            while (pos < bufEnd)
            {
                var inFastLimit = inEnd - 8;
                if (inPos <= inFastLimit)
                {
                    // ── FAST PATH: 3-level loop (outer → refill → inner) ──
                    while (true)
                    {
                        // Refill to ≥56 bits
                        if (bitsCount < 56)
                        {
                            if (inPos <= inFastLimit)
                            {
                                bits |= *(ulong*)(inp + inPos) << bitsCount;
                                inPos += (63 - bitsCount) >> 3;
                                bitsCount |= 56;
                            }
                            else { break; }
                        }

                        // ── Inner loop: decode symbols without refill ──
                        do
                        {
                            // Decode lit/len (two-level table)
                            var entry = ll[bits & llMask];
                            var len = (int)(entry & 0xFF);
                            var sym = (int)(entry >> 8);
                            if ((len & 0x40) != 0)
                            {
                                entry = ll[sym + ((uint)(bits >> DeflateDecoderWorkspace.PrimaryBits) & ((1u << (len & 0x3F)) - 1))];
                                len = (int)(entry & 0xFF) + DeflateDecoderWorkspace.PrimaryBits;
                                sym = (int)(entry >> 8);
                            }
                            bits >>= len; bitsCount -= len;

                            if (sym >= 256)
                            {
                                if (sym == 256)
                                    goto DynSave256;

                                // ── Match decode ──
                                // Need up to 5(extraLen)+15(dist two-level)+13(extraDist) = 33 bits.
                                if (bitsCount < 33)
                                {
                                    if (inPos <= inFastLimit)
                                    {
                                        bits |= *(ulong*)(inp + inPos) << bitsCount;
                                        inPos += (63 - bitsCount) >> 3;
                                        bitsCount |= 56;
                                    }
                                    else
                                    {
                                        while (bitsCount <= 56 && inPos < inEnd)
                                        { bits |= (ulong)inp[inPos++] << bitsCount; bitsCount += 8; }
                                    }
                                }

                                var lengthCode = sym - 257;
                                if ((uint)lengthCode > 28u)
                                    goto DynInvalidData;

                                var lp = Unsafe.Add(ref lengthPacked, lengthCode);
                                var matchLen = (int)(lp & 0xFFFF) + (int)(bits & ((1u << (int)(lp >> 16)) - 1));
                                bits >>= (int)(lp >> 16);
                                bitsCount -= (int)(lp >> 16);

                                // Distance decode (two-level table)
                                var distEntry = ds[bits & dsMask];
                                var distLen = (int)(distEntry & 0xFF);
                                var distCode = (int)(distEntry >> 8);
                                if ((distLen & 0x40) != 0)
                                {
                                    distEntry = ds[distCode + ((uint)(bits >> DeflateDecoderWorkspace.PrimaryBits) & ((1u << (distLen & 0x3F)) - 1))];
                                    distLen = (int)(distEntry & 0xFF) + DeflateDecoderWorkspace.PrimaryBits;
                                    distCode = (int)(distEntry >> 8);
                                }
                                bits >>= distLen; bitsCount -= distLen;

                                if ((uint)distCode > 29u)
                                    goto DynInvalidData;

                                var dp = Unsafe.Add(ref distancePacked, distCode);
                                var distance = (int)(dp & 0xFFFF) + (int)(bits & ((1u << (int)(dp >> 16)) - 1));
                                bits >>= (int)(dp >> 16);
                                bitsCount -= (int)(dp >> 16);

                                // Copy match
                                var src = buf + pos - distance;
                                var dst = buf + pos;
                                pos += matchLen;

                                if (distance >= 16)
                                {
                                    Vector128.LoadUnsafe(ref *src).StoreUnsafe(ref *dst);
                                    if (matchLen > 16)
                                    {
                                        var mEnd = dst + matchLen;
                                        dst += 16; src += 16;
                                        do { Vector128.LoadUnsafe(ref *src).StoreUnsafe(ref *dst); dst += 16; src += 16; } while (dst < mEnd);
                                    }
                                }
                                else if (distance >= 8)
                                {
                                    *(ulong*)dst = *(ulong*)src;
                                    if (matchLen > 8)
                                    {
                                        *(ulong*)(dst + 8) = *(ulong*)(src + 8);
                                        if (matchLen > 16)
                                        {
                                            var mEnd = dst + matchLen;
                                            dst += 16; src += 16;
                                            do { *(ulong*)dst = *(ulong*)src; dst += 8; src += 8; } while (dst < mEnd);
                                        }
                                    }
                                }
                                else if (distance == 1)
                                {
                                    Unsafe.InitBlockUnaligned(dst, *src, (uint)matchLen);
                                }
                                else
                                {
                                    ulong pat;
                                    if (distance == 4)
                                    {
                                        pat = *(uint*)src;
                                        pat |= pat << 32;
                                    }
                                    else if (distance == 2)
                                    {
                                        pat = *(ushort*)src;
                                        pat |= pat << 16;
                                        pat |= pat << 32;
                                    }
                                    else
                                    {
                                        pat = (ulong)src[0] | ((ulong)src[1] << 8);
                                        pat |= (ulong)src[2 % distance] << 16;
                                        pat |= (ulong)src[3 % distance] << 24;
                                        pat |= (ulong)src[4 % distance] << 32;
                                        pat |= (ulong)src[5 % distance] << 40;
                                        pat |= (ulong)src[6 % distance] << 48;
                                        pat |= (ulong)src[7 % distance] << 56;
                                    }
                                    *(ulong*)dst = pat;
                                    if (matchLen > 8)
                                    {
                                        var mEnd = dst + matchLen;
                                        dst += 8;
                                        do { *(ulong*)dst = pat; dst += 8; } while (dst < mEnd);
                                    }
                                }

                                // After match, break to outer for refill
                                break;
                            }

                            buf[pos++] = (byte)sym;
                        }
                        while (bitsCount >= 15 && pos < bufEnd);

                        if (pos >= bufEnd)
                            goto DynSave;
                    }

                    if (pos >= bufEnd)
                        goto DynSave;

                    // Clear dirty bits above bitsCount before entering slow path
                    if (bitsCount < 64)
                        bits &= (1UL << bitsCount) - 1;
                }

                // --- SLOW PATH: refill from stream ---
                if (inPos >= inEnd && bitsCount < 15)
                {
                    bufferPos = pos;
                    inputPos = inPos;
                    bitBuffer = bits;
                    bitsInBuffer = bitsCount;
                    RefillInput();

                    bits = bitBuffer;
                    bitsCount = bitsInBuffer;
                    inPos = inputPos;
                    inEnd = inputEnd;

                    if (inEnd == 0 && bitsCount == 0)
                        return pos - startPos;

                    if (inEnd > 0)
                        continue;
                }

                // Byte-by-byte refill + decode one symbol
                while (bitsCount <= 56 && inPos < inEnd)
                {
                    bits |= (ulong)inp[inPos++] << bitsCount;
                    bitsCount += 8;
                }

                // Decode one symbol (two-level table)
                var entryS = ll[bits & llMask];
                var lenS = (int)(entryS & 0xFF);
                var symS = (int)(entryS >> 8);
                if ((lenS & 0x40) != 0)
                {
                    bits >>= DeflateDecoderWorkspace.PrimaryBits;
                    bitsCount -= DeflateDecoderWorkspace.PrimaryBits;
                    entryS = ll[symS + ((uint)bits & ((1u << (lenS & 0x3F)) - 1))];
                    lenS = (int)(entryS & 0xFF);
                    symS = (int)(entryS >> 8);
                }
                bits >>= lenS; bitsCount -= lenS;

                if (symS < 256)
                {
                    buf[pos++] = (byte)symS;
                    continue;
                }

                if (symS == 256)
                {
                    bufferPos = pos;
                    inputPos = inPos;
                    bitBuffer = bits;
                    bitsInBuffer = bitsCount;
                    isBlockActive = false;
                    return pos - startPos;
                }

                // Match in slow path
                var lengthCodeS = symS - 257;
                if ((uint)lengthCodeS > 28u) goto DynInvalidData;

                var lpS = Unsafe.Add(ref lengthPacked, lengthCodeS);
                var matchLenS = (int)(lpS & 0xFFFF);
                var extraLenS = (int)(lpS >> 16);

                {
                    // Need extraLen + max 15(dist) + 13(extraDist) = up to 33 bits
                    var needed = extraLenS + 15 + 13;
                    if (bitsCount < needed)
                    {
                        while (bitsCount <= 56 && inPos < inEnd)
                        { bits |= (ulong)inp[inPos++] << bitsCount; bitsCount += 8; }

                        if (bitsCount < needed && inPos >= inEnd)
                        {
                            bufferPos = pos; inputPos = inPos; bitBuffer = bits; bitsInBuffer = bitsCount;
                            RefillInput();
                            bits = bitBuffer; bitsCount = bitsInBuffer; inPos = inputPos; inEnd = inputEnd;
                            while (bitsCount <= 56 && inPos < inEnd)
                            { bits |= (ulong)inp[inPos++] << bitsCount; bitsCount += 8; }
                        }
                    }
                }

                if (extraLenS != 0)
                {
                    matchLenS += (int)(bits & ((1u << extraLenS) - 1));
                    bits >>= extraLenS; bitsCount -= extraLenS;
                }

                while (bitsCount <= 56 && inPos < inEnd)
                {
                    bits |= (ulong)inp[inPos++] << bitsCount;
                    bitsCount += 8;
                }

                // Distance decode (two-level table)
                var distEntryS = ds[bits & dsMask];
                var distLenS = (int)(distEntryS & 0xFF);
                var distCodeS = (int)(distEntryS >> 8);
                if ((distLenS & 0x40) != 0)
                {
                    bits >>= DeflateDecoderWorkspace.PrimaryBits;
                    bitsCount -= DeflateDecoderWorkspace.PrimaryBits;
                    distEntryS = ds[distCodeS + ((uint)bits & ((1u << (distLenS & 0x3F)) - 1))];
                    distLenS = (int)(distEntryS & 0xFF);
                    distCodeS = (int)(distEntryS >> 8);
                }
                bits >>= distLenS; bitsCount -= distLenS;

                if ((uint)distCodeS > 29u) goto DynInvalidData;

                var dpS = Unsafe.Add(ref distancePacked, distCodeS);
                var distanceS = (int)(dpS & 0xFFFF);
                var extraDistS = (int)(dpS >> 16);
                if (extraDistS != 0)
                {
                    distanceS += (int)(bits & ((1u << extraDistS) - 1));
                    bits >>= extraDistS; bitsCount -= extraDistS;
                }

                var srcS = buf + pos - distanceS;
                var dstS = buf + pos;
                pos += matchLenS;

                if (distanceS >= 8)
                {
                    while (matchLenS >= 8) { *(ulong*)dstS = *(ulong*)srcS; dstS += 8; srcS += 8; matchLenS -= 8; }
                    if (matchLenS > 0) *(ulong*)dstS = *(ulong*)srcS;
                }
                else if (distanceS == 1)
                {
                    var fillS = (ulong)*srcS * 0x0101010101010101UL;
                    while (matchLenS >= 8) { *(ulong*)dstS = fillS; dstS += 8; matchLenS -= 8; }
                    while (matchLenS-- > 0) *dstS++ = *srcS;
                }
                else
                {
                    ulong patS;
                    if (distanceS == 4) { patS = *(uint*)srcS; patS |= patS << 32; }
                    else if (distanceS == 2) { patS = *(ushort*)srcS; patS |= patS << 16; patS |= patS << 32; }
                    else
                    {
                        patS = (ulong)srcS[0] | ((ulong)srcS[1] << 8);
                        patS |= (ulong)srcS[2 % distanceS] << 16;
                        patS |= (ulong)srcS[3 % distanceS] << 24;
                        patS |= (ulong)srcS[4 % distanceS] << 32;
                        patS |= (ulong)srcS[5 % distanceS] << 40;
                        patS |= (ulong)srcS[6 % distanceS] << 48;
                        patS |= (ulong)srcS[7 % distanceS] << 56;
                    }
                    *(ulong*)dstS = patS;
                    if (matchLenS > 8)
                    {
                        var mEndS = dstS + matchLenS;
                        dstS += 8;
                        do { *(ulong*)dstS = patS; dstS += 8; } while (dstS < mEndS);
                    }
                }
            }

        DynSave:
            if (bitsCount < 64)
                bits &= (1UL << bitsCount) - 1;
            bufferPos = pos;
            inputPos = inPos;
            bitBuffer = bits;
            bitsInBuffer = bitsCount;
            return pos - startPos;

        DynSave256:
            if (bitsCount < 64)
                bits &= (1UL << bitsCount) - 1;
            bufferPos = pos;
            inputPos = inPos;
            bitBuffer = bits;
            bitsInBuffer = bitsCount;
            isBlockActive = false;
            return pos - startPos;

        DynInvalidData:
            bufferPos = pos;
            inputPos = inPos;
            bitBuffer = bits;
            bitsInBuffer = bitsCount;
            throw new InvalidDataException("Invalid Deflate data");
        }
    }

    #endregion

    #region Bit Buffer

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RefillInput()
    {
        inputEnd = input.Read(new Span<byte>(inputPtr, ws.InputCapacity));
        inputPos = 0;

        // Zero-padding для безопасного *(ulong*) на границе буфера
        *(ulong*)(inputPtr + inputEnd) = 0;

        while (bitsInBuffer <= 56 && inputPos < inputEnd)
        {
            bitBuffer |= (ulong)inputPtr[inputPos++] << bitsInBuffer;
            bitsInBuffer += 8;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool EnsureBits(int count)
    {
        if (bitsInBuffer >= count) return true;

        while (bitsInBuffer < count)
        {
            if (inputPos >= inputEnd)
            {
                inputEnd = input.Read(new Span<byte>(inputPtr, ws.InputCapacity));
                inputPos = 0;

                // Zero-padding для безопасного *(ulong*) на границе буфера
                *(ulong*)(inputPtr + inputEnd) = 0;

                if (inputEnd == 0) return false;
            }
            bitBuffer |= (ulong)inputPtr[inputPos++] << bitsInBuffer;
            bitsInBuffer += 8;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ConsumeBits(int count)
    {
        bitBuffer >>= count;
        bitsInBuffer -= count;
    }

    #endregion

    #region Dispose

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        if (ownsWorkspace)
        {
            DeflateDecoderWorkspacePool.Return(ws);
        }
    }

    #endregion

    #region Types

    private enum BlockType
    {
        Stored = 0,
        FixedHuffman = 1,
        DynamicHuffman = 2,
    }

    #endregion
}
