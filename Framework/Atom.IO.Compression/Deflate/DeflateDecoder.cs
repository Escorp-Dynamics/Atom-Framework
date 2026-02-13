#pragma warning disable CA2213, MA0051, S3776, CA1857, S109, S907, S1199, S1854, S3626, IDE0004, IDE0055, S1751, MA0076, MA0182, CS0649, S3459, MA0071, IDE0044

using System.Runtime.CompilerServices;

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
        var tableLogLitLen = CalculateTableLog(litLenLengths, 11); // Ограничиваем 11 битами для L1 cache
        // hlit - полное количество символов для canonical code calculation
        // 286 - maxValidSymbol (символы 286-287 reserved, их в таблицу не добавляем)
        // 256 - defaultSymbol (EOB) для пустых ячеек в lit/len таблице
        litLenMask = BuildHuffmanTablePtr(litLenLengths, hlit, dynLitLenPtr, tableLogLitLen, 286, 256);
        litLenPtr = dynLitLenPtr;

        var distLengths = new ReadOnlySpan<byte>(codeLengths + hlit, hdist);
        var tableLogDist = CalculateTableLog(distLengths, 11); // Ограничиваем 11 битами
        // hdist - полное количество символов для canonical code calculation
        // 30 - maxValidSymbol (distance codes 30-31 reserved, их в таблицу не добавляем)
        // 0 - defaultSymbol для пустых ячеек в distance таблице
        distMask = BuildHuffmanTablePtr(distLengths, hdist, dynDistPtr, tableLogDist, 30, 0);
        distPtr = dynDistPtr;

        return true;
    }

    private static int CalculateTableLog(ReadOnlySpan<byte> codeLengths, int maxLog)
    {
        var maxLen = 0;
        for (var i = 0; i < codeLengths.Length; i++)
        {
            if (codeLengths[i] > maxLen)
                maxLen = codeLengths[i];
        }
        return Math.Min(maxLen, maxLog);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int BuildHuffmanTable(ReadOnlySpan<byte> codeLengths, int numSymbols, Span<uint> table, int tableLog)
    {
        var tableSize = 1 << tableLog;
        var mask = tableSize - 1;

        // Инициализируем таблицу — быстрое заполнение
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

        // Canonical Huffman code generation
        Span<int> nextCode = stackalloc int[16];
        var code = 0;
        for (var bits = 1; bits <= 15; bits++)
        {
            code = (code + blCount[bits - 1]) << 1;
            nextCode[bits] = code;
        }

        // Быстрый reverse через lookup
        ref var bitRev = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(DeflateTables.BitReverse8);

        for (var sym = 0; sym < actualSymbols; sym++)
        {
            var len = codeLengths[sym];
            if (len is 0 or > 15) continue;

            var huffCode = nextCode[len]++;
            var fillBits = tableLog - len;
            if (fillBits < 0) continue;

            // Inline reverse bits
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

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int BuildHuffmanTablePtr(ReadOnlySpan<byte> codeLengths, int numSymbols, uint* table, int tableLog, int maxValidSymbol = int.MaxValue, int defaultSymbol = 0)
    {
        // Защита от пустых таблиц
        if (tableLog == 0)
            tableLog = 1;

        var tableSize = 1 << tableLog;
        var mask = tableSize - 1;

        // Инициализируем таблицу символом fallback — быстрое заполнение 8 байт
        var defaultEntry = ((uint)defaultSymbol << 8) | (uint)tableLog;
        var defaultEntry64 = defaultEntry | ((ulong)defaultEntry << 32);
        var tableLongs = tableSize >> 1;
        var tablePtr64 = (ulong*)table;
        for (var i = 0; i < tableLongs; i++)
            tablePtr64[i] = defaultEntry64;

        var actualSymbols = Math.Min(numSymbols, codeLengths.Length);

        // Подсчёт кодов по длинам — используем ref для скорости
        Span<int> blCount = stackalloc int[16];
        blCount.Clear();
        ref var clRef = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(codeLengths);
        for (var i = 0; i < actualSymbols; i++)
        {
            var cl = Unsafe.Add(ref clRef, i);
            if (cl is > 0 and <= 15)
                blCount[cl]++;
        }

        // Canonical Huffman code generation
        Span<int> nextCode = stackalloc int[16];
        var code = 0;
        for (var bits = 1; bits <= 15; bits++)
        {
            code = (code + blCount[bits - 1]) << 1;
            nextCode[bits] = code;
        }

        // Быстрый reverse через lookup
        ref var bitRev = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(DeflateTables.BitReverse8);

        for (var sym = 0; sym < actualSymbols; sym++)
        {
            var len = Unsafe.Add(ref clRef, sym);
            if (len is 0 or > 15) continue;

            var huffCode = nextCode[len]++;

            // Пропускаем невалидные символы
            if (sym >= maxValidSymbol) continue;

            var fillBits = tableLog - len;
            if (fillBits < 0) continue;

            // Inline reverse bits через lookup (до 16 бит)
            var reversed = (Unsafe.Add(ref bitRev, huffCode & 0xFF) << 8)
                         | Unsafe.Add(ref bitRev, (huffCode >> 8) & 0xFF);
            var baseIndex = reversed >> (16 - len);

            var entry = (uint)((sym << 8) | len);
            var fillCount = 1 << fillBits;

            // Заполняем все вхождения (unrolled для малых fillCount)
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

        return mask;
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
            BlockType.FixedHuffman or BlockType.DynamicHuffman => DecodeHuffmanBlock(),
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

            // Prefetch packed tables
            ref var lengthPacked = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(DeflateTables.LengthPacked);
            ref var distancePacked = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(DeflateTables.DistancePacked);

            // Границы для fast path
            var inFastEnd = inEnd - 8;

            while (pos < bufEnd)
            {
                // ============================================================
                // FAST PATH: пока достаточно входных данных
                // ============================================================
                while (inPos <= inFastEnd && pos < bufEnd)
                {
                    // Refill до 56+ бит
                    var bytesToAdd = (64 - bitsCount) >> 3;
                    bits |= *(ulong*)(inp + inPos) << bitsCount;
                    inPos += bytesToAdd;
                    bitsCount += bytesToAdd << 3;

                    // Decode lit/len symbol
                    var entry = ll[bits & llMask];
                    var len = (int)(entry & 0xFF);
                    var sym = (int)(entry >> 8);
                    bits >>= len; bitsCount -= len;

                    // Literal — самый частый случай
                    if (sym < 256)
                    {
                        buf[pos++] = (byte)sym;
                        continue;
                    }

                    // End of block
                    if (sym == 256)
                    {
                        bufferPos = pos;
                        inputPos = inPos;
                        bitBuffer = bits;
                        bitsInBuffer = bitsCount;
                        isBlockActive = false;
                        return pos - startPos;
                    }

                    // Match decode
                    var lengthCode = sym - 257;
                    if ((uint)lengthCode > 28u) goto InvalidData;

                    var lp = Unsafe.Add(ref lengthPacked, lengthCode);
                    var matchLen = (int)(lp & 0xFFFF);
                    var extraLenBits = (int)(lp >> 16);

                    matchLen += (int)(bits & ((1u << extraLenBits) - 1));
                    bits >>= extraLenBits;
                    bitsCount -= extraLenBits;

                    // Refill for distance
                    var bytesToAddD = (64 - bitsCount) >> 3;
                    bits |= *(ulong*)(inp + inPos) << bitsCount;
                    inPos += bytesToAddD;
                    bitsCount += bytesToAddD << 3;

                    var distEntry = ds[bits & dsMask];
                    var distLen = (int)(distEntry & 0xFF);
                    var distCode = (int)(distEntry >> 8);
                    bits >>= distLen; bitsCount -= distLen;

                    if ((uint)distCode > 29u) goto InvalidData;

                    var dp = Unsafe.Add(ref distancePacked, distCode);
                    var distance = (int)(dp & 0xFFFF);
                    var extraDistBits = (int)(dp >> 16);

                    // Branch-free extra bits extraction
                    distance += (int)(bits & ((1u << extraDistBits) - 1));
                    bits >>= extraDistBits;
                    bitsCount -= extraDistBits;

                    // Copy match — optimized for common cases
                    var src = buf + pos - distance;
                    var dst = buf + pos;
                    pos += matchLen;

                    if (distance >= 8)
                    {
                        // Non-overlapping: unrolled 32-byte copy
                        while (matchLen >= 32)
                        {
                            *(ulong*)dst = *(ulong*)src;
                            *(ulong*)(dst + 8) = *(ulong*)(src + 8);
                            *(ulong*)(dst + 16) = *(ulong*)(src + 16);
                            *(ulong*)(dst + 24) = *(ulong*)(src + 24);
                            dst += 32; src += 32; matchLen -= 32;
                        }
                        while (matchLen >= 8) { *(ulong*)dst = *(ulong*)src; dst += 8; src += 8; matchLen -= 8; }
                        if (matchLen > 0) *(ulong*)dst = *(ulong*)src;
                    }
                    else if (distance == 1)
                    {
                        var fill = (ulong)*src * 0x0101010101010101UL;
                        while (matchLen >= 8) { *(ulong*)dst = fill; dst += 8; matchLen -= 8; }
                        while (matchLen-- > 0) *dst++ = *src;
                    }
                    else
                    {
                        while (matchLen-- > 0) *dst++ = *src++;
                    }
                }

                // ============================================================
                // SLOW PATH: мало входных данных — refill из stream
                // ============================================================
                if (inPos >= inEnd)
                {
                    // Если ещё есть биты, попробуем декодировать
                    if (bitsCount >= 15) // минимум для lit/len + dist в fixed Huffman
                    {
                        // Декодируем с оставшимися битами
                        // (ниже идёт byte-by-byte decode)
                    }
                    else
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
                        inFastEnd = inEnd - 8;

                        // Если stream пуст
                        if (inEnd == 0)
                        {
                            // Если совсем нет битов — выходим
                            if (bitsCount == 0)
                                return pos - startPos;
                            // Иначе декодируем оставшиеся биты ниже
                        }
                        else
                        {
                            continue;
                        }
                    }
                }

                // Byte-by-byte refill для оставшихся данных
                while (bitsCount <= 56 && inPos < inEnd)
                {
                    bits |= (ulong)inp[inPos++] << bitsCount;
                    bitsCount += 8;
                }

                // Decode one symbol
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
                if ((uint)lengthCodeS > 28u) goto InvalidData;

                var lpS = Unsafe.Add(ref lengthPacked, lengthCodeS);
                var matchLenS = (int)(lpS & 0xFFFF);
                var extraLenS = (int)(lpS >> 16);
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

                var distEntryS = ds[bits & dsMask];
                var distLenS = (int)(distEntryS & 0xFF);
                var distCodeS = (int)(distEntryS >> 8);
                bits >>= distLenS; bitsCount -= distLenS;

                if ((uint)distCodeS > 29u) goto InvalidData;

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
                    while (matchLenS-- > 0) *dstS++ = *srcS++;
                }
            }

            bufferPos = pos;
            inputPos = inPos;
            bitBuffer = bits;
            bitsInBuffer = bitsCount;
            return pos - startPos;

        InvalidData:
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
