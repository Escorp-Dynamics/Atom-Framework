#pragma warning disable CA1062, CA2213, MA0042, MA0051, MA0140, S1871, S3923, S3776, S109

using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Atom.IO.Compression.Deflate;

/// <summary>
/// Энкодер Deflate (RFC 1951) с LZ77 + Huffman.
/// </summary>
/// <remarks>
/// Стратегии:
/// - Fastest: Stored blocks only (без сжатия)
/// - Optimal: Fixed Huffman + lazy matching
/// - SmallestSize: Dynamic Huffman + optimal parsing
///
/// Реализация полностью unsafe с pointer-арифметикой для максимальной производительности.
/// </remarks>
[SkipLocalsInit]
internal sealed unsafe class DeflateEncoder : IDisposable
{
    #region Constants

    private const int WindowSize = 32768;
    private const int WindowMask = WindowSize - 1;
    private const int MinMatch = 3;
    private const int MaxMatch = 258;
    private const int MaxDistance = 32768;
    private const int HashBits = 15;
    private const int HashSize = 1 << HashBits;
    private const int MaxChainLength = 128;
    private const int BlockSize = 32768;
    private const int OutputBufferSize = 8192;

    #endregion

    #region Static Tables

    /// <summary>
    /// Precomputed Fixed Huffman codes (already reversed, ready to write).
    /// [symbol] = (code, length)
    /// </summary>
    private static readonly uint[] FixedLitLenCodes = CreateFixedLitLenCodes();

    private static uint[] CreateFixedLitLenCodes()
    {
        var table = new uint[288]; // 0-287

        for (var symbol = 0; symbol <= 143; symbol++)
        {
            var code = (uint)(symbol + 0x30);
            var reversed = ReverseBits(code, 8);
            table[symbol] = reversed | (8u << 16);
        }

        for (var symbol = 144; symbol <= 255; symbol++)
        {
            var code = (uint)(symbol - 144 + 0x190);
            var reversed = ReverseBits(code, 9);
            table[symbol] = reversed | (9u << 16);
        }

        for (var symbol = 256; symbol <= 279; symbol++)
        {
            var code = (uint)(symbol - 256);
            var reversed = ReverseBits(code, 7);
            table[symbol] = reversed | (7u << 16);
        }

        for (var symbol = 280; symbol <= 287; symbol++)
        {
            var code = (uint)(symbol - 280 + 0xC0);
            var reversed = ReverseBits(code, 8);
            table[symbol] = reversed | (8u << 16);
        }

        return table;
    }

    private static uint ReverseBits(uint value, int count)
    {
        uint result = 0;
        for (var i = 0; i < count; i++)
        {
            result = (result << 1) | (value & 1);
            value >>= 1;
        }
        return result;
    }

    /// <summary>
    /// Precomputed reversed distance codes (5 bits, all same length).
    /// </summary>
    private static readonly byte[] ReversedDistCodes = CreateReversedDistCodes();

    private static byte[] CreateReversedDistCodes()
    {
        var table = new byte[30];
        for (var i = 0; i < 30; i++)
        {
            table[i] = (byte)ReverseBits((uint)i, 5);
        }
        return table;
    }

    #endregion

    #region Fields

    private readonly System.IO.Stream output;
    private readonly CompressionLevel level;

    // Окно данных (history + lookahead)
    private readonly byte[] window;
    private int windowPos;
    private int lookaheadSize;

    // Hash chain для LZ77
    private readonly int[] hashHead;
    private readonly int[] hashPrev;

    // Буфер литералов/матчей для блока
    private readonly ushort[] litLenBuffer;
    private readonly ushort[] distBuffer;
    private int bufferPos;

    // Битовый вывод с буферизацией
    private ulong bitBuffer;  // 64-bit для меньшего количества flush
    private int bitsInBuffer;
    private readonly byte[] outputBuffer;
    private int outputPos;

    // Lazy matching state
    private int prevLength;
    private int prevDistance;
    private byte prevLiteral;  // Байт на позиции где был найден match
    private bool hasPrevMatch;

    // Fastest streaming state
    private bool fastestBlockStarted;

    // Состояние
    private bool isFinished;
    private bool isDisposed;

    #endregion

    #region Constructor

    public DeflateEncoder(System.IO.Stream output, CompressionLevel level)
    {
        this.output = output;
        this.level = level;

        window = new byte[WindowSize * 2];  // history + lookahead
        outputBuffer = new byte[OutputBufferSize];

        // Аллоцируем hash только если нужно сжатие
        if (level != CompressionLevel.NoCompression)
        {
            hashHead = new int[HashSize];
            hashPrev = new int[WindowSize];
            litLenBuffer = new ushort[BlockSize];
            distBuffer = new ushort[BlockSize];

            // Инициализация hash heads — нужно для корректности
            Array.Fill(hashHead, -1);
        }
        else
        {
            hashHead = [];
            hashPrev = [];
            litLenBuffer = [];
            distBuffer = [];
        }
    }

    #endregion

    #region Public Methods

    public void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return;

        // Быстрый путь для NoCompression — пишем stored blocks напрямую
        if (level == CompressionLevel.NoCompression)
        {
            WriteNoCompressionDirect(buffer);
            return;
        }

        var offset = 0;
        while (offset < buffer.Length)
        {
            // Заполняем lookahead
            var available = Math.Min(buffer.Length - offset, WindowSize - lookaheadSize);
            buffer.Slice(offset, available).CopyTo(window.AsSpan(windowPos + lookaheadSize));
            lookaheadSize += available;
            offset += available;

            // Специальный быстрый путь для Fastest
            if (level == CompressionLevel.Fastest)
            {
                ProcessFastestBulk();
                continue;
            }

            // Сжимаем, пока lookahead достаточно
            while (lookaheadSize >= MinMatch)
            {
                ProcessByte();

                // Если буфер блока заполнен — пишем блок
                if (bufferPos >= BlockSize)
                {
                    WriteBlock(isFinal: false);
                }
            }
        }
    }

    /// <summary>
    /// Ultra-fast NoCompression: записывает stored blocks напрямую без промежуточных буферов.
    /// </summary>
    private void WriteNoCompressionDirect(ReadOnlySpan<byte> buffer)
    {
        // Максимальный размер stored block = 65535 байт
        const int maxStoredBlockSize = 65535;

        var offset = 0;
        while (offset < buffer.Length)
        {
            var remaining = buffer.Length - offset;
            var blockSize = Math.Min(remaining, maxStoredBlockSize);

            // BFINAL=0 (не финальный), BTYPE=00 (stored)
            WriteBits(0, 1);
            WriteBits(0, 2);

            // Выравнивание на границу байта
            FlushBits();

            // LEN и NLEN (unchecked для побитовой инверсии)
            unchecked
            {
                var len = (ushort)blockSize;
                var nlen = (ushort)~len;

                WriteByteBuffered((byte)(len & 0xFF));
                WriteByteBuffered((byte)(len >> 8));
                WriteByteBuffered((byte)(nlen & 0xFF));
                WriteByteBuffered((byte)(nlen >> 8));
            }

            // Записываем данные блока
            var blockData = buffer.Slice(offset, blockSize);
            WriteBlockData(blockData);

            offset += blockSize;
        }
    }

    /// <summary>
    /// Записывает данные блока напрямую в выходной поток.
    /// </summary>
    private void WriteBlockData(ReadOnlySpan<byte> data)
    {
        var dataOffset = 0;
        while (dataOffset < data.Length)
        {
            // Сколько места осталось в буфере
            var spaceInBuffer = outputBuffer.Length - outputPos;
            if (spaceInBuffer == 0)
            {
                // Flush буфер
                output.Write(outputBuffer.AsSpan(0, outputPos));
                outputPos = 0;
                spaceInBuffer = outputBuffer.Length;
            }

            // Копируем сколько можем
            var toCopy = Math.Min(data.Length - dataOffset, spaceInBuffer);
            data.Slice(dataOffset, toCopy).CopyTo(outputBuffer.AsSpan(outputPos));
            outputPos += toCopy;
            dataOffset += toCopy;
        }
    }

    /// <summary>
    /// Ultra-fast streaming для Fastest — inline Huffman без промежуточных буферов.
    /// </summary>
    private void ProcessFastestBulk()
    {
        fixed (byte* windowPtr = window)
        fixed (int* hashHeadPtr = hashHead)
        fixed (uint* fixedCodesPtr = FixedLitLenCodes)
        fixed (byte* revDistPtr = ReversedDistCodes)
        fixed (byte* outBufPtr = outputBuffer)
        {
            var pos = windowPos;
            var remaining = lookaheadSize;

            // Если нечего обрабатывать или это маленький хвост — оставляем для Finish
            // (чтобы короткие данные попали в финальный блок без лишнего пустого блока)
            if (remaining < MinMatch && !fastestBlockStarted)
            {
                return;
            }

            // Записываем заголовок блока если ещё не начали и есть данные
            if (!fastestBlockStarted)
            {
                // BFINAL=0, BTYPE=01 (Fixed Huffman)
                WriteBits(0b010, 3);
                fastestBlockStarted = true;
            }

            var bits = bitBuffer;
            var bitsCount = bitsInBuffer;
            var outPos = outputPos;

            unchecked
            {
                while (remaining >= MinMatch)
                {
                    // Hash lookup
                    var v = *(uint*)(windowPtr + pos);
                    var hash = (int)(((v & 0xFFFFFF) * 0x1E35A7BDu) >> (32 - HashBits));
                    var matchPos = hashHeadPtr[hash];
                    hashHeadPtr[hash] = pos;

                    var distance = pos - matchPos;
                    if (matchPos >= 0 && distance > 0 && distance <= MaxDistance && *(uint*)(windowPtr + matchPos) == v)
                    {
                        // Match found — extend it
                        var p1 = windowPtr + matchPos;
                        var p2 = windowPtr + pos;
                        var max = remaining < 66 ? remaining : 66;
                        var len = 4;

                        while (len + 8 <= max && *(ulong*)(p1 + len) == *(ulong*)(p2 + len))
                            len += 8;
                        while (len < max && p1[len] == p2[len])
                            len++;

                        // Inline: записываем length code
                        var lenCode = DeflateTables.LengthToCode[len];
                        var encoded = fixedCodesPtr[lenCode];
                        bits |= (ulong)(encoded & 0xFFFF) << bitsCount;
                        bitsCount += (int)(encoded >> 16);

                        // Length extra bits
                        var baseCode = lenCode - 257;
                        var extraBits = DeflateTables.LengthExtraBits[baseCode];
                        if (extraBits > 0)
                        {
                            var extra = len - DeflateTables.LengthBase[baseCode];
                            bits |= (ulong)(uint)extra << bitsCount;
                            bitsCount += extraBits;
                        }

                        // Distance code
                        var distCode = DeflateTables.GetDistanceCode(distance);
                        bits |= (ulong)revDistPtr[distCode] << bitsCount;
                        bitsCount += 5;

                        // Distance extra bits
                        var distExtraBits = DeflateTables.DistanceExtraBits[distCode];
                        if (distExtraBits > 0)
                        {
                            var distExtra = distance - DeflateTables.DistanceBase[distCode];
                            bits |= (ulong)(uint)distExtra << bitsCount;
                            bitsCount += distExtraBits;
                        }

                        pos += len;
                        remaining -= len;
                    }
                    else
                    {
                        // Literal — inline encoding
                        var encoded = fixedCodesPtr[windowPtr[pos]];
                        bits |= (ulong)(encoded & 0xFFFF) << bitsCount;
                        bitsCount += (int)(encoded >> 16);
                        pos++;
                        remaining--;
                    }

                    // Bulk flush 32+ bits
                    if (bitsCount >= 32)
                    {
                        *(uint*)(outBufPtr + outPos) = (uint)bits;
                        bits >>= 32;
                        bitsCount -= 32;
                        outPos += 4;

                        if (outPos >= OutputBufferSize - 8)
                        {
                            outputPos = outPos;
                            output.Write(outputBuffer.AsSpan(0, outPos));
                            outPos = 0;
                        }
                    }
                }

                // Финальные литералы
                while (remaining > 0)
                {
                    var encoded = fixedCodesPtr[windowPtr[pos]];
                    bits |= (ulong)(encoded & 0xFFFF) << bitsCount;
                    bitsCount += (int)(encoded >> 16);
                    pos++;
                    remaining--;

                    if (bitsCount >= 32)
                    {
                        *(uint*)(outBufPtr + outPos) = (uint)bits;
                        bits >>= 32;
                        bitsCount -= 32;
                        outPos += 4;

                        if (outPos >= OutputBufferSize - 8)
                        {
                            outputPos = outPos;
                            output.Write(outputBuffer.AsSpan(0, outPos));
                            outPos = 0;
                        }
                    }
                }
            }

            bitBuffer = bits;
            bitsInBuffer = bitsCount;
            outputPos = outPos;
            windowPos = pos;
            lookaheadSize = remaining;
        }

        // Window shift — без сброса hash (переиспользуем)
        if (windowPos >= WindowSize)
        {
            Buffer.BlockCopy(window, WindowSize, window, 0, WindowSize);
            windowPos -= WindowSize;
            // Обновляем hash heads вместо сброса
            UpdateHashTablesSimd();
        }
    }

    /// <summary>
    /// SIMD обновление hash tables при window shift.
    /// </summary>
    private void UpdateHashTablesSimd()
    {
        fixed (int* ptr = hashHead)
        {
            if (Avx2.IsSupported)
            {
                var shift = Vector256.Create(WindowSize);
                var zero = Vector256<int>.Zero;

                for (var i = 0; i < HashSize; i += 8)
                {
                    var v = Avx.LoadVector256(ptr + i);
                    v = Avx2.Subtract(v, shift);
                    v = Avx2.Max(v, zero);
                    Avx.Store(ptr + i, v);
                }
            }
            else if (Sse2.IsSupported)
            {
                var shift = Vector128.Create(WindowSize);
                var zero = Vector128<int>.Zero;

                for (var i = 0; i < HashSize; i += 4)
                {
                    var v = Sse2.LoadVector128(ptr + i);
                    v = Sse2.Subtract(v, shift);
                    // SSE2 не имеет Max для int, используем сравнение
                    var mask = Sse2.CompareGreaterThan(v, zero);
                    v = Sse2.And(v, mask);
                    Sse2.Store(ptr + i, v);
                }
            }
            else
            {
                for (var i = 0; i < HashSize; i++)
                {
                    ptr[i] = Math.Max(0, ptr[i] - WindowSize);
                }
            }
        }
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken _ = default)
    {
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public void Flush()
    {
        if (bufferPos > 0)
        {
            WriteBlock(isFinal: false);
        }

        FlushBits();
        output.Flush();
    }

    public ValueTask FlushAsync(CancellationToken _ = default)
    {
        Flush();
        return ValueTask.CompletedTask;
    }

    public void Finish()
    {
        if (isFinished) return;

        // Специальная обработка для Fastest — streaming mode
        if (level == CompressionLevel.Fastest)
        {
            FinishFastest();
            return;
        }

        // Специальная обработка для NoCompression — пишем финальный пустой stored block
        if (level == CompressionLevel.NoCompression)
        {
            FinishNoCompression();
            return;
        }

        fixed (byte* windowPtr = window)
        {
            // Сжимаем оставшийся lookahead
            while (lookaheadSize > 0)
            {
                if (lookaheadSize >= MinMatch)
                {
                    ProcessByte();
                }
                else
                {
                    // Перед записью литералов нужно flush pending lazy match
                    if (hasPrevMatch)
                    {
                        // Записываем prevLiteral, потому что мы не можем использовать
                        // отложенный матч (недостаточно данных для сравнения)
                        EmitLiteral(prevLiteral);
                        hasPrevMatch = false;
                    }

                    EmitLiteral(windowPtr[windowPos]);
                    AdvanceWindow(1);
                }
            }
        }

        // Flush pending lazy match (если цикл завершился после ProcessByte)
        if (hasPrevMatch)
        {
            EmitLiteral(prevLiteral);
            hasPrevMatch = false;
        }

        // Пишем финальный блок
        WriteBlock(isFinal: true);
        FlushBits();
        output.Flush();

        isFinished = true;
    }

    /// <summary>
    /// Финализация streaming Fastest mode.
    /// </summary>
    private void FinishFastest()
    {
        if (fastestBlockStarted)
        {
            // Записываем оставшиеся литералы если есть
            while (lookaheadSize > 0)
            {
                var encoded = FixedLitLenCodes[window[windowPos]];
                WriteBits(encoded & 0xFFFF, (int)(encoded >> 16));
                windowPos++;
                lookaheadSize--;
            }

            // Блок уже начат с BFINAL=0, записываем EOB
            var eobCode = FixedLitLenCodes[256];
            WriteBits(eobCode & 0xFFFF, (int)(eobCode >> 16));

            // Пустой финальный блок: BFINAL=1, BTYPE=01, EOB
            WriteBits(0b011, 3);
            var finalEob = FixedLitLenCodes[256];
            WriteBits(finalEob & 0xFFFF, (int)(finalEob >> 16));
        }
        else
        {
            // Блок не начинался — пишем один финальный блок с данными
            // BFINAL=1, BTYPE=01
            WriteBits(0b011, 3);

            // Записываем оставшиеся литералы
            while (lookaheadSize > 0)
            {
                var encoded = FixedLitLenCodes[window[windowPos]];
                WriteBits(encoded & 0xFFFF, (int)(encoded >> 16));
                windowPos++;
                lookaheadSize--;
            }

            // EOB
            var eobCode = FixedLitLenCodes[256];
            WriteBits(eobCode & 0xFFFF, (int)(eobCode >> 16));
        }

        FlushBits();
        output.Flush();
        isFinished = true;
    }

    /// <summary>
    /// Финализация для NoCompression — пишем финальный пустой stored block.
    /// </summary>
    private void FinishNoCompression()
    {
        // Записываем пустой финальный stored block: BFINAL=1, BTYPE=00
        WriteBits(1, 1); // BFINAL = 1
        WriteBits(0, 2); // BTYPE = 00 (stored)

        // Выравнивание на границу байта
        FlushBits();

        // LEN=0, NLEN=0xFFFF
        WriteByteBuffered(0);
        WriteByteBuffered(0);
        WriteByteBuffered(0xFF);
        WriteByteBuffered(0xFF);

        // Flush output buffer
        if (outputPos > 0)
        {
            output.Write(outputBuffer.AsSpan(0, outputPos));
            outputPos = 0;
        }

        output.Flush();
        isFinished = true;
    }

    public ValueTask FinishAsync(CancellationToken _ = default)
    {
        Finish();
        return ValueTask.CompletedTask;
    }

    #endregion

    #region LZ77 Matching

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessByte()
    {
        // Специальный быстрый путь для Fastest - минимум overhead
        if (level == CompressionLevel.Fastest)
        {
            ProcessByteFastest();
            return;
        }

        fixed (byte* windowPtr = window)
        {
            if (level == CompressionLevel.NoCompression)
            {
                EmitLiteral(windowPtr[windowPos]);
                AdvanceWindow(1);
                return;
            }

            var (length, distance) = FindMatch();

            // Lazy matching только для SmallestSize (улучшает сжатие, но медленнее)
            if (hasPrevMatch)
            {
                if (length > prevLength)
                {
                    // Новый матч лучше - выводим предыдущий литерал
                    EmitLiteral(prevLiteral);

                    if (length >= MinMatch)
                    {
                        // Сохраняем новый матч для проверки на следующей позиции
                        prevLength = length;
                        prevDistance = distance;
                        prevLiteral = windowPtr[windowPos];
                        AdvanceWindow(1);
                        return;
                    }

                    // Новый матч слишком короткий — записываем текущий байт как литерал
                    hasPrevMatch = false;
                    EmitLiteral(windowPtr[windowPos]);
                    AdvanceWindow(1);
                    return;
                }

                // Предыдущий матч лучше или равен - выводим его
                EmitMatch(prevLength, prevDistance);
                hasPrevMatch = false;
                var skip = prevLength - 1;
                for (var i = 0; i < skip && lookaheadSize > 0; i++)
                    AdvanceWindow(1);
                return;
            }

            if (length >= MinMatch)
            {
                // Lazy matching только для SmallestSize (максимальное сжатие)
                if (level == CompressionLevel.SmallestSize)
                {
                    hasPrevMatch = true;
                    prevLength = length;
                    prevDistance = distance;
                    prevLiteral = windowPtr[windowPos];
                    AdvanceWindow(1);
                    return;
                }

                EmitMatch(length, distance);
                AdvanceWindow(length);
            }
            else
            {
                EmitLiteral(windowPtr[windowPos]);
                AdvanceWindow(1);
            }
        }
    }

    /// <summary>
    /// Оптимизированный ProcessByte для Fastest: greedy matching без hash chain traversal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessByteFastest()
    {
        if (lookaheadSize < MinMatch)
        {
            if (lookaheadSize > 0)
            {
                fixed (byte* windowPtr = window)
                {
                    EmitLiteral(windowPtr[windowPos]);
                }
                AdvanceWindowFast(1);
            }
            return;
        }

        fixed (byte* windowPtr = window)
        {
            var hash = ComputeHashPtr(windowPtr, windowPos);
            var matchPos = hashHead[hash];
            var distance = windowPos - matchPos;

            // Обновляем hash chain сразу
            hashPrev[windowPos & WindowMask] = matchPos;
            hashHead[hash] = windowPos;

            // Greedy: проверяем только первый матч (без chain traversal)
            // Быстрая проверка первых 4 байт + валидация позиции
            if (matchPos >= 0 && distance <= MaxDistance && distance > 0 &&
                *(uint*)(windowPtr + matchPos) == *(uint*)(windowPtr + windowPos))
            {
                var len = MatchLengthPtr(windowPtr, matchPos, windowPos);
                if (len >= MinMatch)
                {
                    EmitMatch(len, distance);
                    AdvanceWindowFast(len);
                    return;
                }
            }

            EmitLiteral(windowPtr[windowPos]);
            AdvanceWindowFast(1);
        }
    }

    /// <summary>
    /// Быстрый AdvanceWindow для Fastest — без промежуточных хешей.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AdvanceWindowFast(int count)
    {
        windowPos += count;
        lookaheadSize -= count;

        // Shift window при переполнении
        if (windowPos >= WindowSize)
        {
            Buffer.BlockCopy(window, WindowSize, window, 0, WindowSize);
            windowPos -= WindowSize;
            // Для Fastest просто сбрасываем hash tables — быстрее чем обновлять
            Array.Fill(hashHead, -1);
        }
    }

    /// <summary>
    /// Вычисление хеша с использованием уже pinned pointer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeHashPtr(byte* windowPtr, int pos)
    {
        var v = *(uint*)(windowPtr + pos);
        return unchecked((int)(((v & 0xFFFFFF) * 0x1E35A7BDu) >> (32 - HashBits)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (int length, int distance) FindMatch()
    {
        if (lookaheadSize < MinMatch) return (0, 0);

        var hash = ComputeHash(windowPos);

        // Параметры в зависимости от уровня сжатия (соответствие zlib)
        // Fastest: быстрый поиск с малой глубиной
        // Optimal: соответствует zlib level 6 (default)
        // SmallestSize: максимальная глубина поиска
        int chainLength, goodLength, niceLength;
        if (level == CompressionLevel.Fastest)
        {
            chainLength = 4;
            goodLength = 4;
            niceLength = 8;
        }
        else if (level == CompressionLevel.SmallestSize)
        {
            chainLength = MaxChainLength << 1; // 256 для максимального сжатия
            goodLength = 32;
            niceLength = MaxMatch; // 258
        }
        else // Optimal
        {
            chainLength = MaxChainLength; // 128
            goodLength = 8; // zlib default
            niceLength = 128; // zlib default
        }

        var bestLength = MinMatch - 1;
        var bestDistance = 0;

        var matchPos = hashHead[hash];
        var limit = Math.Max(windowPos - MaxDistance, 0);

        fixed (byte* windowPtr = window)
        fixed (int* hashPrevPtr = hashPrev)
        {
            while (matchPos >= limit && chainLength-- > 0)
            {
                var distance = windowPos - matchPos;
                if (distance > MaxDistance) break;

                // Quick check: сравниваем байт на позиции bestLength первым
                if (bestLength >= MinMatch && windowPtr[matchPos + bestLength] != windowPtr[windowPos + bestLength])
                {
                    matchPos = hashPrevPtr[matchPos & WindowMask];
                    continue;
                }

                var len = MatchLengthPtr(windowPtr, matchPos, windowPos);
                if (len > bestLength)
                {
                    bestLength = len;
                    bestDistance = distance;

                    // Выход при достаточно хорошем матче (niceLength)
                    if (len >= niceLength) break;

                    // Сокращаем поиск при хорошем матче (zlib-style)
                    if (len >= goodLength) chainLength >>= 1;
                }

                matchPos = hashPrevPtr[matchPos & WindowMask];
            }
        }

        // Обновляем hash chain
        hashPrev[windowPos & WindowMask] = hashHead[hash];
        hashHead[hash] = windowPos;

        return bestLength >= MinMatch ? (bestLength, bestDistance) : (0, 0);
    }

    /// <summary>
    /// Вычисление длины совпадения с использованием уже pinned pointer.
    /// Избегает overhead повторного fixed в hot-path.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int MatchLengthPtr(byte* windowPtr, int s1, int s2)
    {
        var max = Math.Min(lookaheadSize, MaxMatch);

        // Ограничиваем max чтобы не выйти за границы window при SIMD чтении
        var s1Max = WindowSize - s1;
        var s2Max = WindowSize - s2;
        max = Math.Min(max, Math.Min(s1Max, s2Max));

        var p1 = windowPtr + s1;
        var p2 = windowPtr + s2;
        var len = 0;

        // Быстрый путь для коротких матчей (большинство в deflate)
        // Первые 8 байт через ulong — покрывает ~90% случаев
        if (max >= 8)
        {
            var v1 = *(ulong*)p1;
            var v2 = *(ulong*)p2;
            if (v1 != v2)
            {
                var diff = v1 ^ v2;
                return Math.Min(BitOperations.TrailingZeroCount(diff) >> 3, max);
            }
            len = 8;
        }

        // AVX2: 32 байта за итерацию для длинных совпадений
        if (Avx2.IsSupported)
        {
            while (len + 32 <= max)
            {
                var v1 = Avx.LoadVector256(p1 + len);
                var v2 = Avx.LoadVector256(p2 + len);
                var cmp = Avx2.CompareEqual(v1, v2);
                var mask = unchecked((uint)Avx2.MoveMask(cmp));

                if (mask != 0xFFFF_FFFF)
                {
                    len += BitOperations.TrailingZeroCount(~mask);
                    return Math.Min(len, max);
                }

                len += 32;
            }
        }

        // SSE2: 16 байт за итерацию
        if (Sse2.IsSupported)
        {
            while (len + 16 <= max)
            {
                var v1 = Sse2.LoadVector128(p1 + len);
                var v2 = Sse2.LoadVector128(p2 + len);
                var cmp = Sse2.CompareEqual(v1, v2);
                var mask = unchecked((uint)Sse2.MoveMask(cmp));

                if (mask != 0xFFFF)
                {
                    len += BitOperations.TrailingZeroCount(~mask & 0xFFFF);
                    return Math.Min(len, max);
                }

                len += 16;
            }
        }

        // Fallback: 64-bit сравнение
        while (len + 8 <= max)
        {
            var v1 = *(ulong*)(p1 + len);
            var v2 = *(ulong*)(p2 + len);

            if (v1 != v2)
            {
                var diff = v1 ^ v2;
                len += BitOperations.TrailingZeroCount(diff) >> 3;
                return Math.Min(len, max);
            }

            len += 8;
        }

        // Остаток побайтово
        while (len < max && p1[len] == p2[len])
            len++;

        return len;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int MatchLength(int s1, int s2)
    {
        var max = Math.Min(lookaheadSize, MaxMatch);

        // Ограничиваем max чтобы не выйти за границы window при SIMD чтении
        var s1Max = WindowSize - s1;
        var s2Max = WindowSize - s2;
        max = Math.Min(max, Math.Min(s1Max, s2Max));

        var len = 0;

        fixed (byte* windowPtr = window)
        {
            var p1 = windowPtr + s1;
            var p2 = windowPtr + s2;

            // AVX2: 32 байта за итерацию
            if (Avx2.IsSupported)
            {
                while (len + 32 <= max)
                {
                    var v1 = Avx.LoadVector256(p1 + len);
                    var v2 = Avx.LoadVector256(p2 + len);
                    var cmp = Avx2.CompareEqual(v1, v2);
                    var mask = unchecked((uint)Avx2.MoveMask(cmp));

                    if (mask != 0xFFFF_FFFF)
                    {
                        len += BitOperations.TrailingZeroCount(~mask);
                        return Math.Min(len, max);
                    }

                    len += 32;
                }
            }

            // SSE2: 16 байт за итерацию
            if (Sse2.IsSupported)
            {
                while (len + 16 <= max)
                {
                    var v1 = Sse2.LoadVector128(p1 + len);
                    var v2 = Sse2.LoadVector128(p2 + len);
                    var cmp = Sse2.CompareEqual(v1, v2);
                    var mask = unchecked((uint)Sse2.MoveMask(cmp));

                    if (mask != 0xFFFF)
                    {
                        len += BitOperations.TrailingZeroCount(~mask & 0xFFFF);
                        return Math.Min(len, max);
                    }

                    len += 16;
                }
            }

            // Fallback: 64-bit сравнение
            while (len + 8 <= max)
            {
                var v1 = *(ulong*)(p1 + len);
                var v2 = *(ulong*)(p2 + len);

                if (v1 != v2)
                {
                    var diff = v1 ^ v2;
                    len += BitOperations.TrailingZeroCount(diff) >> 3;
                    return Math.Min(len, max);
                }

                len += 8;
            }

            // Остаток побайтово
            while (len < max && p1[len] == p2[len])
                len++;
        }

        return len;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ComputeHash(int pos)
    {
        fixed (byte* windowPtr = window)
        {
            var v = *(uint*)(windowPtr + pos);
            return unchecked((int)(((v & 0xFFFFFF) * 0x1E35A7BDu) >> (32 - HashBits)));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AdvanceWindow(int count)
    {
        // При match > 1, вставляем промежуточные хеши для лучшего сжатия
        // Только для SmallestSize (максимальное сжатие, можно жертвовать скоростью)
        if (count > 1 && level == CompressionLevel.SmallestSize)
        {
            for (var i = 1; i < count && windowPos + i + 2 < windowPos + lookaheadSize; i++)
            {
                var hash = ComputeHash(windowPos + i);
                hashPrev[(windowPos + i) & WindowMask] = hashHead[hash];
                hashHead[hash] = windowPos + i;
            }
        }

        windowPos += count;
        lookaheadSize -= count;

        // Shift window при переполнении
        if (windowPos >= WindowSize)
        {
            // Копируем вторую половину в первую
            Buffer.BlockCopy(window, WindowSize, window, 0, WindowSize);
            windowPos -= WindowSize;

            // Обновляем hash heads с SIMD где возможно
            UpdateHashTables();
        }
    }

    private void UpdateHashTables()
    {
        fixed (int* headPtr = hashHead, prevPtr = hashPrev)
        {
            var i = 0;

            // Векторизованное обновление - по 8 элементов за раз
            var subtractVec = Vector256.Create(WindowSize);
            var minVec = Vector256.Create(-1);

            while (i + 8 <= HashSize)
            {
                var vec = Vector256.Load(headPtr + i);
                vec = Vector256.Subtract(vec, subtractVec);
                vec = Vector256.Max(vec, minVec);
                vec.Store(headPtr + i);
                i += 8;
            }

            // Остаток hashHead
            for (; i < HashSize; i++)
            {
                var val = headPtr[i] - WindowSize;
                headPtr[i] = val < -1 ? -1 : val;
            }

            i = 0;
            while (i + 8 <= WindowSize)
            {
                var vec = Vector256.Load(prevPtr + i);
                vec = Vector256.Subtract(vec, subtractVec);
                vec = Vector256.Max(vec, minVec);
                vec.Store(prevPtr + i);
                i += 8;
            }

            // Остаток hashPrev
            for (; i < WindowSize; i++)
            {
                var val = prevPtr[i] - WindowSize;
                prevPtr[i] = val < -1 ? -1 : val;
            }
        }
    }

    #endregion

    #region Symbol Buffer

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EmitLiteral(byte value)
    {
        fixed (ushort* litPtr = litLenBuffer, distPtr = distBuffer)
        {
            litPtr[bufferPos] = value;
            distPtr[bufferPos] = 0;
            bufferPos++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EmitMatch(int length, int distance)
    {
        fixed (ushort* litPtr = litLenBuffer, distPtr = distBuffer)
        {
            litPtr[bufferPos] = (ushort)(length + 256);
            distPtr[bufferPos] = (ushort)distance;
            bufferPos++;
        }
    }

    #endregion

    #region Block Writing

    private void WriteBlock(bool isFinal)
    {
        if (bufferPos == 0 && !isFinal) return;

        // Выбираем тип блока
        if (level == CompressionLevel.NoCompression)
        {
            WriteStoredBlock(isFinal);
        }
        else
        {
            WriteFixedHuffmanBlock(isFinal);
        }

        bufferPos = 0;
    }

    private void WriteStoredBlock(bool isFinal)
    {
        // BFINAL + BTYPE=00
        WriteBits(isFinal ? 1u : 0u, 1);
        WriteBits(0, 2);

        // Выравнивание на границу байта
        FlushBits();

        // LEN + NLEN + data
        var len = bufferPos & 0xFFFF;
        var nlen = (~len) & 0xFFFF;

        WriteByteBuffered((byte)(len & 0xFF));
        WriteByteBuffered((byte)(len >> 8));
        WriteByteBuffered((byte)(nlen & 0xFF));
        WriteByteBuffered((byte)(nlen >> 8));

        fixed (ushort* litPtr = litLenBuffer)
        {
            for (var i = 0; i < bufferPos; i++)
                WriteByteBuffered((byte)litPtr[i]);
        }
    }

    private void WriteFixedHuffmanBlock(bool isFinal)
    {
        // BFINAL + BTYPE=01
        WriteBits(isFinal ? 1u : 0u, 1);
        WriteBits(1, 2);

        // Локальные копии для избежания field access в hot loop
        var localBitBuffer = bitBuffer;
        var localBitsInBuffer = bitsInBuffer;

        fixed (ushort* litPtr = litLenBuffer, distPtr = distBuffer)
        fixed (uint* fixedCodesPtr = FixedLitLenCodes)
        fixed (byte* revDistPtr = ReversedDistCodes)
        fixed (byte* outBufPtr = outputBuffer)
        {
            var outPos = outputPos;

            unchecked
            {
                for (var i = 0; i < bufferPos; i++)
                {
                    var litLen = litPtr[i];
                    var dist = distPtr[i];

                    if (dist == 0)
                    {
                        // Literal — inline
                        var encoded = fixedCodesPtr[litLen];
                        localBitBuffer |= (ulong)(encoded & 0xFFFF) << localBitsInBuffer;
                        localBitsInBuffer += (int)(encoded >> 16);
                    }
                    else
                    {
                        // Match — inline
                        var length = litLen - 256;
                        var lengthCode = DeflateTables.LengthToCode[length];

                        // Write length code
                        var encoded = fixedCodesPtr[lengthCode];
                        localBitBuffer |= (ulong)(encoded & 0xFFFF) << localBitsInBuffer;
                        localBitsInBuffer += (int)(encoded >> 16);

                        // Length extra bits
                        var baseCode = lengthCode - 257;
                        var extraBits = DeflateTables.LengthExtraBits[baseCode];
                        if (extraBits > 0)
                        {
                            var extra = length - DeflateTables.LengthBase[baseCode];
                            localBitBuffer |= (ulong)(uint)extra << localBitsInBuffer;
                            localBitsInBuffer += extraBits;
                        }

                        // Distance code
                        var distCode = DeflateTables.GetDistanceCode(dist);
                        localBitBuffer |= (ulong)revDistPtr[distCode] << localBitsInBuffer;
                        localBitsInBuffer += 5;

                        // Distance extra bits
                        var distExtraBits = DeflateTables.DistanceExtraBits[distCode];
                        if (distExtraBits > 0)
                        {
                            var distExtra = dist - DeflateTables.DistanceBase[distCode];
                            localBitBuffer |= (ulong)(uint)distExtra << localBitsInBuffer;
                            localBitsInBuffer += distExtraBits;
                        }
                    }

                    // Flush 32+ bits inline
                    if (localBitsInBuffer >= 32)
                    {
                        if (outPos + 4 > OutputBufferSize)
                        {
                            outputPos = outPos;
                            bitBuffer = localBitBuffer;
                            bitsInBuffer = localBitsInBuffer;
                            FlushOutputBuffer();
                            outPos = outputPos;
                        }

                        *(uint*)(outBufPtr + outPos) = (uint)localBitBuffer;
                        outPos += 4;
                        localBitBuffer >>= 32;
                        localBitsInBuffer -= 32;
                    }
                }
            }

            outputPos = outPos;
        }

        // Sync back
        bitBuffer = localBitBuffer;
        bitsInBuffer = localBitsInBuffer;

        // End of block (256)
        WriteFixedLiteral(256);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteFixedLiteral(int symbol)
    {
        // Используем precomputed table: FixedLitLenCodes[symbol] = code | (length << 16)
        var encoded = FixedLitLenCodes[symbol];
        var code = encoded & 0xFFFF;
        var length = (int)(encoded >> 16);
        WriteBits(code, length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteFixedLength(int code, int length)
    {
        WriteFixedLiteral(code);

        // Extra bits
        var baseCode = code - 257;
        var extraBits = DeflateTables.LengthExtraBits[baseCode];
        if (extraBits > 0)
        {
            var extra = length - DeflateTables.LengthBase[baseCode];
            WriteBits((uint)extra, extraBits);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteFixedDistance(int distance)
    {
        var code = DeflateTables.GetDistanceCode(distance);

        // Fixed distance: 5 бит, используем precomputed reversed codes
        WriteBits(ReversedDistCodes[code], 5);

        // Extra bits
        var extraBits = DeflateTables.DistanceExtraBits[code];
        if (extraBits > 0)
        {
            var extra = distance - DeflateTables.DistanceBase[code];
            WriteBits((uint)extra, extraBits);
        }
    }

    #endregion

    #region Bit Output

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteBits(uint bits, int count)
    {
        bitBuffer |= (ulong)bits << bitsInBuffer;
        bitsInBuffer += count;

        // Flush 32+ bits через bulk write
        if (bitsInBuffer >= 32)
        {
            FlushBitBufferFast();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FlushBitBufferFast()
    {
        // Проверяем место в буфере
        if (outputPos + 4 > OutputBufferSize)
        {
            FlushOutputBuffer();
        }

        // Записываем 4 байта (32 бит) одной операцией
        fixed (byte* outPtr = outputBuffer)
        {
            // Записываем 4 байта как uint (little-endian на x86/x64)
            unchecked
            {
                *(uint*)(outPtr + outputPos) = (uint)bitBuffer;
            }
        }

        outputPos += 4;
        bitBuffer >>= 32;
        bitsInBuffer -= 32;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FlushBitBuffer()
    {
        // Записываем все полные байты
        while (bitsInBuffer >= 8)
        {
            WriteByteBuffered((byte)(bitBuffer & 0xFF));
            bitBuffer >>= 8;
            bitsInBuffer -= 8;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FlushBits()
    {
        // Flush оставшиеся биты побайтово
        while (bitsInBuffer > 0)
        {
            WriteByteBuffered((byte)(bitBuffer & 0xFF));
            bitBuffer >>= 8;
            bitsInBuffer = Math.Max(bitsInBuffer - 8, 0);
        }

        bitsInBuffer = 0;
        bitBuffer = 0;

        // Flush output buffer
        FlushOutputBuffer();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteByteBuffered(byte value)
    {
        outputBuffer[outputPos++] = value;
        if (outputPos >= OutputBufferSize)
        {
            FlushOutputBuffer();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FlushOutputBuffer()
    {
        if (outputPos > 0)
        {
            output.Write(outputBuffer, 0, outputPos);
            outputPos = 0;
        }
    }

    #endregion

    #region Dispose

    public void Dispose()
    {
        if (isDisposed) return;

        if (!isFinished)
        {
            Finish();
        }

        isDisposed = true;
    }

    #endregion
}
