#pragma warning disable CA1062, CA2213, MA0042, MA0051, MA0140, S1871, S3923, S3776, S109, S907, IDE0055

using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Atom.IO.Compression.Huffman;

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
    private const int MaxChainLength = 32;
    private const int BlockSize = 32768;
    private const int OutputBufferSize = 8192; // 64KB — снижает количество Stream.Write вызовов

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

    // NoCompression: буфер для последнего (≤65535) блока, который будет записан с BFINAL=1
    private byte[]? noCompressionPending;
    private int noCompressionPendingLength;

    // Состояние
    private bool isFinished;
    private bool isDisposed;

    #endregion

    #region Constructor

    public DeflateEncoder(System.IO.Stream output, CompressionLevel level)
    {
        this.output = output;
        this.level = level;

        // Pinned arrays — исключаем overhead GC-пиннинга в hot path
        // +32 padding для AVX2 LoadVector256 в match comparison (overread до 32 байт за max)
        window = GC.AllocateArray<byte>((WindowSize * 2) + 32, pinned: true);
        outputBuffer = GC.AllocateArray<byte>(OutputBufferSize, pinned: true);

        // Аллоцируем hash только если нужно сжатие
        if (level != CompressionLevel.NoCompression)
        {
            hashHead = GC.AllocateArray<int>(HashSize, pinned: true);
            hashPrev = GC.AllocateArray<int>(WindowSize, pinned: true);

            litLenBuffer = GC.AllocateArray<ushort>(BlockSize, pinned: true);
            distBuffer = GC.AllocateArray<ushort>(BlockSize, pinned: true);

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

            // Оптимизированный bulk-путь для Optimal/SmallestSize
            ProcessOptimalBulk();
        }
    }

    /// <summary>
    /// Ultra-fast NoCompression: записывает stored blocks напрямую без промежуточных буферов.
    /// Полные 65535-байтные блоки пишутся с BFINAL=0, хвост буферизуется
    /// для записи с BFINAL=1 в FinishNoCompression.
    /// </summary>
    private void WriteNoCompressionDirect(ReadOnlySpan<byte> buffer)
    {
        const int maxStoredBlockSize = 65535;

        // Если есть pending данные, объединяем с новым буфером
        if (noCompressionPendingLength > 0)
        {
            // Проверяем, поместится ли всё в один блок
            var totalPending = noCompressionPendingLength + buffer.Length;
            if (totalPending <= maxStoredBlockSize)
            {
                // Всё помещается — добавляем к pending
                EnsureNoCompressionPendingCapacity(totalPending);
                buffer.CopyTo(noCompressionPending.AsSpan(noCompressionPendingLength));
                noCompressionPendingLength = totalPending;
                return;
            }

            // Не помещается — flush pending как BFINAL=0, продолжаем обработку buffer
            WriteStoredBlockDirect(noCompressionPending.AsSpan(0, noCompressionPendingLength), isFinal: false);
            noCompressionPendingLength = 0;
        }

        var offset = 0;
        while (offset < buffer.Length)
        {
            var remaining = buffer.Length - offset;

            if (remaining <= maxStoredBlockSize)
            {
                // Последний кусок — буферизуем для записи с BFINAL=1 в Finish
                EnsureNoCompressionPendingCapacity(remaining);
                buffer.Slice(offset, remaining).CopyTo(noCompressionPending.AsSpan(0, remaining));
                noCompressionPendingLength = remaining;
                return;
            }

            // Полный блок — пишем с BFINAL=0
            var blockSize = Math.Min(remaining, maxStoredBlockSize);
            WriteStoredBlockDirect(buffer.Slice(offset, blockSize), isFinal: false);
            offset += blockSize;
        }
    }

    /// <summary>
    /// Гарантирует, что noCompressionPending имеет достаточную ёмкость.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureNoCompressionPendingCapacity(int required)
    {
        if (noCompressionPending is null || noCompressionPending.Length < required)
            noCompressionPending = new byte[Math.Max(required, 65535)];
    }

    /// <summary>
    /// Записывает один stored block с указанным BFINAL.
    /// </summary>
    private void WriteStoredBlockDirect(ReadOnlySpan<byte> data, bool isFinal)
    {
        WriteBits(isFinal ? 1u : 0u, 1);
        WriteBits(0, 2);
        FlushBits();

        unchecked
        {
            var len = (ushort)data.Length;
            var nlen = (ushort)~len;

            WriteByteBuffered((byte)(len & 0xFF));
            WriteByteBuffered((byte)(len >> 8));
            WriteByteBuffered((byte)(nlen & 0xFF));
            WriteByteBuffered((byte)(nlen >> 8));
        }

        WriteBlockData(data);
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
                    // Hash lookup (4-byte hash для уменьшения коллизий)
                    var v = *(uint*)(windowPtr + pos);
                    var hash = (int)((v * 0x1E35A7BDu) >> (32 - HashBits));
                    var matchPos = hashHeadPtr[hash];
                    hashHeadPtr[hash] = pos;

                    var distance = pos - matchPos;
                    if (remaining >= 4 && matchPos >= 0 && distance > 0 && distance <= MaxDistance && *(uint*)(windowPtr + matchPos) == v)
                    {
                        // Match found — extend it
                        var p1 = windowPtr + matchPos;
                        var p2 = windowPtr + pos;
                        var max = remaining < MaxMatch ? remaining : MaxMatch;
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

                        // Вставляем хэш для конца match (pos + len - 3): после пропуска match
                        // эта позиция — ближайший актуальный кандидат для следующих lookup'ов
                        if (len >= 4 && remaining - len >= 4)
                        {
                            var tailPos = pos + len - 3;
                            var tailH = (int)((*(uint*)(windowPtr + tailPos) * 0x1E35A7BDu) >> (32 - HashBits));
                            hashHeadPtr[tailH] = tailPos;
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

    /// <summary>
    /// Bulk-обработка для Optimal/SmallestSize — token collection + Dynamic/Fixed Huffman.
    /// Поддерживает hash chain traversal и lazy matching (SmallestSize).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ProcessOptimalBulk()
    {
        // Параметры в зависимости от уровня сжатия
        int chainLen, goodLen, niceLen;
        var useLazy = level == CompressionLevel.SmallestSize;
        if (useLazy)
        {
            // zlib level 9: chain=4096, good=32, nice=258, lazy=258
            // good=32 позволяет сократить chain traversal вдвое при нахождении 32+ byte match
            chainLen = 4096;
            goodLen = 32;
            niceLen = MaxMatch;
        }
        else
        {
            // zlib level 6: chain=32, good=16, nice=128
            chainLen = MaxChainLength; // 32
            goodLen = 16;
            niceLen = 128;
        }

        fixed (byte* windowPtr = window)
        fixed (int* hashHeadPtr = hashHead, hashPrevPtr = hashPrev)
        fixed (ushort* litPtr = litLenBuffer, dstPtr = distBuffer)
        {
            while (lookaheadSize >= MinMatch)
            {
                var pos = windowPos;

                // Inline hash + chain lookup
                var v = *(uint*)(windowPtr + pos);
                var hash = unchecked((int)(((v & 0xFFFFFF) * 0x1E35A7BDu) >> (32 - HashBits)));

                var bestLength = MinMatch - 1;
                var bestDistance = 0;
                var matchPos = hashHeadPtr[hash];
                var limit = Math.Max(pos - MaxDistance, 0);
                var cl = chainLen;

                while (matchPos >= limit && cl-- > 0)
                {
                    var distance = pos - matchPos;
                    if (distance > MaxDistance) break;

                    if (bestLength >= MinMatch && windowPtr[matchPos + bestLength] != windowPtr[pos + bestLength])
                    {
                        matchPos = hashPrevPtr[matchPos & WindowMask];
                        continue;
                    }

                    var max = Math.Min(lookaheadSize, MaxMatch);
                    var p1 = windowPtr + matchPos;
                    var p2 = windowPtr + pos;
                    var len = 0;

                    // SIMD-ускоренное сравнение: 32 байта за шаг (AVX2)
                    if (Avx2.IsSupported && max >= 32)
                    {
                        while (len + 32 <= max)
                        {
                            var v1 = Avx.LoadVector256(p1 + len);
                            var v2 = Avx.LoadVector256(p2 + len);
                            var cmp = Avx2.CompareEqual(v1, v2);
                            var mask = unchecked((uint)Avx2.MoveMask(cmp));
                            if (mask != 0xFFFFFFFFu)
                            {
                                len = Math.Min(len + BitOperations.TrailingZeroCount(~mask), max);
                                goto MatchDone;
                            }
                            len += 32;
                        }
                    }
                    else if (max >= 8)
                    {
                        var v1 = *(ulong*)p1;
                        var v2 = *(ulong*)p2;
                        if (v1 != v2)
                        {
                            len = Math.Min(BitOperations.TrailingZeroCount(v1 ^ v2) >> 3, max);
                            goto MatchDone;
                        }
                        len = 8;
                    }

                    while (len + 8 <= max && *(ulong*)(p1 + len) == *(ulong*)(p2 + len))
                        len += 8;
                    while (len < max && p1[len] == p2[len])
                        len++;

                    MatchDone:
                    if (len > bestLength)
                    {
                        bestLength = len;
                        bestDistance = distance;
                        if (len >= niceLen) break;
                        if (len >= goodLen) cl >>= 1;
                    }

                    matchPos = hashPrevPtr[matchPos & WindowMask];
                }

                // Обновляем hash chain
                hashPrevPtr[pos & WindowMask] = hashHeadPtr[hash];
                hashHeadPtr[hash] = pos;

                // Lazy matching (SmallestSize)
                if (useLazy && hasPrevMatch)
                {
                    if (bestLength > prevLength)
                    {
                        // Новый лучший — emit предыдущий литерал
                        litPtr[bufferPos] = prevLiteral;
                        dstPtr[bufferPos] = 0;
                        bufferPos++;

                        if (bestLength >= MinMatch)
                        {
                            prevLength = bestLength;
                            prevDistance = bestDistance;
                            prevLiteral = windowPtr[pos];
                            windowPos++;
                            lookaheadSize--;
                            goto CheckBlock;
                        }

                        hasPrevMatch = false;
                        litPtr[bufferPos] = windowPtr[pos];
                        dstPtr[bufferPos] = 0;
                        bufferPos++;
                        windowPos++;
                        lookaheadSize--;
                        goto CheckBlock;
                    }

                    // Предыдущий match лучше — emit его
                    litPtr[bufferPos] = (ushort)(prevLength + 256);
                    dstPtr[bufferPos] = (ushort)prevDistance;
                    bufferPos++;
                    hasPrevMatch = false;

                    // Пропускаем позиции внутри предыдущего матча.
                    // Вставляем hash entries для первых 2 позиций в матче,
                    // чтобы улучшить будущие поиски. Больше вставок удлиняет цепочки
                    // и создаёт feedback loop (замедляет последующие поиски на 30-40%).
                    var skip = prevLength - 1;
                    var maxIns = Math.Min(skip - 1, 2);
                    for (var i = 0; i < skip && lookaheadSize > 0; i++)
                    {
                        windowPos++;
                        lookaheadSize--;
                        if (i < maxIns && lookaheadSize >= MinMatch)
                        {
                            var sp = windowPos;
                            var sh = unchecked((int)(((*(uint*)(windowPtr + sp) & 0xFFFFFF) * 0x1E35A7BDu) >> (32 - HashBits)));
                            hashPrevPtr[sp & WindowMask] = hashHeadPtr[sh];
                            hashHeadPtr[sh] = sp;
                        }
                    }
                    goto CheckBlock;
                }

                if (bestLength >= MinMatch)
                {
                    if (useLazy)
                    {
                        hasPrevMatch = true;
                        prevLength = bestLength;
                        prevDistance = bestDistance;
                        prevLiteral = windowPtr[pos];
                        windowPos++;
                        lookaheadSize--;
                    }
                    else
                    {
                        // Greedy emit match
                        litPtr[bufferPos] = (ushort)(bestLength + 256);
                        dstPtr[bufferPos] = (ushort)bestDistance;
                        bufferPos++;

                        // Вставляем позиции внутри матча в hash chain (как zlib level 6).
                        // max_insert_length=goodLen: для коротких матчей обновляем таблицу,
                        // для длинных — пропускаем (цена > выгоды).
                        if (bestLength <= goodLen)
                        {
                            var insertEnd = pos + bestLength - 2; // -2: последние 2 позиции не хешируем (< MinMatch до конца)
                            for (var ip = pos + 1; ip <= insertEnd; ip++)
                            {
                                var ih = unchecked((int)(((*(uint*)(windowPtr + ip) & 0xFFFFFF) * 0x1E35A7BDu) >> (32 - HashBits)));
                                hashPrevPtr[ip & WindowMask] = hashHeadPtr[ih];
                                hashHeadPtr[ih] = ip;
                            }
                        }

                        windowPos += bestLength;
                        lookaheadSize -= bestLength;
                    }
                }
                else
                {
                    litPtr[bufferPos] = windowPtr[pos];
                    dstPtr[bufferPos] = 0;
                    bufferPos++;
                    windowPos++;
                    lookaheadSize--;
                }

                CheckBlock:
                if (bufferPos >= BlockSize)
                    WriteBlock(isFinal: false);

                if (windowPos >= WindowSize)
                {
                    Buffer.BlockCopy(window, WindowSize, window, 0, WindowSize);
                    windowPos -= WindowSize;
                    UpdateHashTablesInline(hashHeadPtr, hashPrevPtr);
                }
            }
        }
    }

    /// <summary>
    /// Inline SIMD обновление hash tables — без повторного fixed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void UpdateHashTablesInline(int* headPtr, int* prevPtr)
    {
        if (Avx2.IsSupported)
        {
            var shift = Vector256.Create(WindowSize);
            var minVal = Vector256.Create(-1);

            for (var i = 0; i < HashSize; i += 8)
            {
                var vec = Avx.LoadVector256(headPtr + i);
                vec = Avx2.Subtract(vec, shift);
                vec = Avx2.Max(vec, minVal);
                Avx.Store(headPtr + i, vec);
            }

            for (var i = 0; i < WindowSize; i += 8)
            {
                var vec = Avx.LoadVector256(prevPtr + i);
                vec = Avx2.Subtract(vec, shift);
                vec = Avx2.Max(vec, minVal);
                Avx.Store(prevPtr + i, vec);
            }
        }
        else
        {
            for (var i = 0; i < HashSize; i++)
            {
                var val = headPtr[i] - WindowSize;
                headPtr[i] = val < -1 ? -1 : val;
            }

            for (var i = 0; i < WindowSize; i++)
            {
                var val = prevPtr[i] - WindowSize;
                prevPtr[i] = val < -1 ? -1 : val;
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

        // Специальная обработка для NoCompression — пишем финальный пустой stored block
        if (level == CompressionLevel.NoCompression)
        {
            FinishNoCompression();
            return;
        }

        // Fastest: inline encoding уже записал все данные, нужно только закрыть блоки
        if (level == CompressionLevel.Fastest)
        {
            FinishFastest();
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
                    // Flush pending lazy match — эмитим сам матч, а не литерал.
                    // Матч уже найден и верифицирован, просто не был записан из-за lazy check.
                    if (hasPrevMatch)
                    {
                        EmitMatch(prevLength, prevDistance);
                        hasPrevMatch = false;
                        var skip = prevLength - 1;
                        for (var i = 0; i < skip && lookaheadSize > 0; i++)
                            AdvanceWindow(1);
                        continue;
                    }

                    EmitLiteral(windowPtr[windowPos]);
                    AdvanceWindow(1);
                }
            }
        }

        // Flush pending lazy match (если цикл завершился после ProcessByte)
        if (hasPrevMatch)
        {
            EmitMatch(prevLength, prevDistance);
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
        // Записываем pending данные как финальный stored block (BFINAL=1)
        if (noCompressionPendingLength > 0)
        {
            WriteStoredBlockDirect(noCompressionPending.AsSpan(0, noCompressionPendingLength), isFinal: true);
            noCompressionPendingLength = 0;
        }
        else
        {
            // Нет pending данных — пустой финальный stored block
            WriteStoredBlockDirect([], isFinal: true);
        }

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

        if (level == CompressionLevel.NoCompression)
        {
            WriteStoredBlock(isFinal);
        }
        else
        {
            // WriteDynamicHuffmanBlock содержит сравнение Fixed vs Dynamic —
            // автоматически выбирает оптимальный вариант для каждого блока.
            WriteDynamicHuffmanBlock(isFinal);
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

        // Safe path using WriteBits — no manual bit buffer management
        for (var i = 0; i < bufferPos; i++)
        {
            var litLen = litLenBuffer[i];
            var dist = distBuffer[i];

            if (dist == 0)
            {
                WriteFixedLiteral(litLen);
            }
            else
            {
                var length = litLen - 256;
                var lengthCode = DeflateTables.LengthToCode[length];
                WriteFixedLength(lengthCode, length);
                WriteFixedDistance(dist);
            }
        }

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

    /// <summary>
    /// Записывает блок с Dynamic Huffman кодированием (BTYPE=10).
    /// Строит оптимальные таблицы из статистики символов блока.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void WriteDynamicHuffmanBlock(bool isFinal)
    {
        // 1. Подсчёт частот символов
        Span<uint> litLenFreqs = stackalloc uint[286];
        Span<uint> distFreqs = stackalloc uint[30];
        litLenFreqs.Clear();
        distFreqs.Clear();

        fixed (ushort* litPtr = litLenBuffer, dPtr = distBuffer)
        {
            for (var i = 0; i < bufferPos; i++)
            {
                if (dPtr[i] == 0)
                {
                    // Литерал
                    litLenFreqs[litPtr[i]]++;
                }
                else
                {
                    // Match: litLenBuffer хранит length + 256
                    var length = litPtr[i] - 256;
                    var lengthCode = DeflateTables.LengthToCode[length];
                    litLenFreqs[lengthCode]++;

                    var distCode = DeflateTables.GetDistanceCode(dPtr[i]);
                    distFreqs[distCode]++;
                }
            }
        }

        litLenFreqs[256] = 1; // End-of-block обязателен

        // Гарантируем хотя бы один distance code (требование спецификации)
        var hasAnyDist = false;
        for (var i = 0; i < 30; i++)
        {
            if (distFreqs[i] > 0) { hasAnyDist = true; break; }
        }
        if (!hasAnyDist) distFreqs[0] = 1;

        // 2. Построение оптимальных длин кодов
        Span<byte> litLenCodeLengths = stackalloc byte[286];
        Span<byte> distCodeLengths = stackalloc byte[30];
        HuffmanTreeBuilder.BuildFromFrequencies(litLenFreqs, litLenCodeLengths, maxCodeLength: 15);
        HuffmanTreeBuilder.BuildFromFrequencies(distFreqs, distCodeLengths, maxCodeLength: 15);

        // 3. Построение reversed кодов (LSB-first для Deflate)
        Span<uint> litLenCodes = stackalloc uint[286];
        Span<uint> distCodesArr = stackalloc uint[30];
        HuffmanTreeBuilder.BuildEncodeCodes(litLenCodeLengths, litLenCodes, lsbFirst: true);
        HuffmanTreeBuilder.BuildEncodeCodes(distCodeLengths, distCodesArr, lsbFirst: true);

        // 4. Определяем HLIT и HDIST (обрезаем trailing zeros)
        var hlit = 286;
        while (hlit > 257 && litLenCodeLengths[hlit - 1] == 0) hlit--;

        var hdist = 30;
        while (hdist > 1 && distCodeLengths[hdist - 1] == 0) hdist--;

        // 5. RLE-кодирование объединённых длин кодов
        var totalCodes = hlit + hdist;
        Span<byte> combinedLengths = stackalloc byte[totalCodes];
        litLenCodeLengths[..hlit].CopyTo(combinedLengths);
        distCodeLengths[..hdist].CopyTo(combinedLengths[hlit..]);

        // RLE-кодирование (worst case: каждый символ отдельно)
        Span<byte> rleCodes = stackalloc byte[totalCodes * 2];
        Span<byte> rleExtra = stackalloc byte[totalCodes * 2];
        Span<byte> rleExtraBits = stackalloc byte[totalCodes * 2];
        var rleCount = RleEncodeCodeLengths(combinedLengths[..totalCodes], rleCodes, rleExtra, rleExtraBits);

        // 6. Частоты code length алфавита и построение кодов
        Span<uint> clFreqs = stackalloc uint[19];
        clFreqs.Clear();
        for (var i = 0; i < rleCount; i++)
            clFreqs[rleCodes[i]]++;

        Span<byte> clCodeLengths = stackalloc byte[19];
        HuffmanTreeBuilder.BuildFromFrequencies(clFreqs, clCodeLengths, maxCodeLength: 7);

        Span<uint> clCodes = stackalloc uint[19];
        HuffmanTreeBuilder.BuildEncodeCodes(clCodeLengths, clCodes, lsbFirst: true);

        // 7. Определяем HCLEN (обрезаем trailing zeros в порядке спецификации)
        ReadOnlySpan<int> clOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];
        var hclen = 19;
        while (hclen > 4 && clCodeLengths[clOrder[hclen - 1]] == 0) hclen--;

        // 7b. Оцениваем размер Dynamic vs Fixed Huffman и выбираем лучший вариант
        // Dynamic: заголовок + данные
        var dynamicHeaderBits = 3 + 14 + (hclen * 3); // BFINAL + BTYPE + HLIT/HDIST/HCLEN + CL lengths
        for (var i = 0; i < rleCount; i++)
            dynamicHeaderBits += clCodeLengths[rleCodes[i]] + rleExtraBits[i];

        var dynamicDataBits = 0;
        for (var s = 0; s < 286; s++)
        {
            if (litLenFreqs[s] > 0)
                dynamicDataBits += (int)litLenFreqs[s] * litLenCodeLengths[s];
        }
        for (var s = 0; s < 30; s++)
        {
            if (distFreqs[s] > 0)
                dynamicDataBits += (int)distFreqs[s] * distCodeLengths[s];
        }

        // Экстра-биты для length и distance (одинаковы для Fixed и Dynamic)
        var extraBitsCost = 0;
        fixed (ushort* litP = litLenBuffer, distP = distBuffer)
        {
            for (var i = 0; i < bufferPos; i++)
            {
                if (distP[i] != 0)
                {
                    var len = litP[i] - 256;
                    extraBitsCost += DeflateTables.LengthExtraBits[DeflateTables.LengthToCode[len] - 257];
                    extraBitsCost += DeflateTables.DistanceExtraBits[DeflateTables.GetDistanceCode(distP[i])];
                }
            }
        }

        var dynamicTotalBits = dynamicHeaderBits + dynamicDataBits + extraBitsCost;

        // Fixed Huffman: 3 бита header + fixed code lengths
        var fixedDataBits = 3; // BFINAL + BTYPE
        for (var s = 0; s < 286; s++)
        {
            if (litLenFreqs[s] > 0)
            {
                // Fixed Huffman code lengths: 0-143→8, 144-255→9, 256-279→7, 280-287→8
                var fixedLen = GetFixedLitLenCodeLength(s);
                fixedDataBits += (int)litLenFreqs[s] * fixedLen;
            }
        }
        // Distance codes в Fixed Huffman всегда 5 бит
        for (var s = 0; s < 30; s++)
        {
            if (distFreqs[s] > 0)
                fixedDataBits += (int)distFreqs[s] * 5;
        }
        fixedDataBits += extraBitsCost;

        // Выбираем меньший вариант
        if (fixedDataBits <= dynamicTotalBits)
        {
            // Fixed дешевле — перенаправляем
            WriteFixedHuffmanBlock(isFinal);
            return;
        }

        // 8. Записываем заголовок блока: BFINAL + BTYPE=10
        WriteBits(isFinal ? 1u : 0u, 1);
        WriteBits(2, 2); // BTYPE=10 (Dynamic Huffman)

        // HLIT, HDIST, HCLEN
        WriteBits((uint)(hlit - 257), 5);
        WriteBits((uint)(hdist - 1), 5);
        WriteBits((uint)(hclen - 4), 4);

        // 9. Записываем длины кодов code length алфавита (в порядке спецификации)
        for (var i = 0; i < hclen; i++)
            WriteBits(clCodeLengths[clOrder[i]], 3);

        // 10. Записываем RLE-кодированные длины кодов
        for (var i = 0; i < rleCount; i++)
        {
            var code = rleCodes[i];
            WriteBits(clCodes[code], clCodeLengths[code]);
            if (rleExtraBits[i] > 0)
                WriteBits(rleExtra[i], rleExtraBits[i]);
        }

        // 11. Записываем данные блока с Dynamic Huffman кодами
        WriteDynamicBlockData(litLenCodes, litLenCodeLengths, distCodesArr, distCodeLengths);

        // 12. End-of-block
        WriteBits(litLenCodes[256], litLenCodeLengths[256]);
    }

    /// <summary>
    /// Длина кода Fixed Huffman для lit/len символа (RFC 1951 §3.2.6).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetFixedLitLenCodeLength(int symbol)
    {
        if (symbol <= 143) return 8;
        if (symbol <= 255) return 9;
        return symbol <= 279 ? 7 : 8;
    }

    /// <summary>
    /// RLE-кодирование массива длин кодов для Dynamic Huffman заголовка (RFC 1951 §3.2.7).
    /// Использует специальные коды: 16 (повтор), 17 (нули 3-10), 18 (нули 11-138).
    /// </summary>
    private static int RleEncodeCodeLengths(
        ReadOnlySpan<byte> lengths,
        Span<byte> codes,
        Span<byte> extra,
        Span<byte> extraBits)
    {
        var count = 0;
        var i = 0;

        while (i < lengths.Length)
        {
            var cur = lengths[i];

            if (cur == 0)
            {
                // Считаем последовательные нули
                var run = 1;
                while (i + run < lengths.Length && lengths[i + run] == 0 && run < 138)
                    run++;

                if (run >= 11)
                {
                    // Код 18: повтор 0 для 11-138 раз (7 extra bits)
                    codes[count] = 18;
                    extra[count] = (byte)(run - 11);
                    extraBits[count] = 7;
                    count++;
                    i += run;
                }
                else if (run >= 3)
                {
                    // Код 17: повтор 0 для 3-10 раз (3 extra bits)
                    codes[count] = 17;
                    extra[count] = (byte)(run - 3);
                    extraBits[count] = 3;
                    count++;
                    i += run;
                }
                else
                {
                    // Отдельные нули
                    for (var j = 0; j < run; j++)
                    {
                        codes[count] = 0;
                        extra[count] = 0;
                        extraBits[count] = 0;
                        count++;
                    }
                    i += run;
                }
            }
            else
            {
                // Записываем значение
                codes[count] = cur;
                extra[count] = 0;
                extraBits[count] = 0;
                count++;
                i++;

                // Проверяем повторы
                var run = 0;
                while (i + run < lengths.Length && lengths[i + run] == cur && run < 6)
                    run++;

                if (run >= 3)
                {
                    // Код 16: повтор предыдущего 3-6 раз (2 extra bits)
                    codes[count] = 16;
                    extra[count] = (byte)(run - 3);
                    extraBits[count] = 2;
                    count++;
                    i += run;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Записывает данные блока с Dynamic Huffman кодами — оптимизированный hot loop.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void WriteDynamicBlockData(
        ReadOnlySpan<uint> llCodes, ReadOnlySpan<byte> llLengths,
        ReadOnlySpan<uint> dCodes, ReadOnlySpan<byte> dLengths)
    {
        // Safe path using WriteBits — no manual bit buffer management
        for (var i = 0; i < bufferPos; i++)
        {
            var litLen = litLenBuffer[i];
            var dist = distBuffer[i];

            if (dist == 0)
            {
                WriteBits(llCodes[litLen], llLengths[litLen]);
            }
            else
            {
                var length = litLen - 256;
                var lengthCode = DeflateTables.LengthToCode[length];
                WriteBits(llCodes[lengthCode], llLengths[lengthCode]);

                var baseCode = lengthCode - 257;
                var extraBitsLen = DeflateTables.LengthExtraBits[baseCode];
                if (extraBitsLen > 0)
                    WriteBits((uint)(length - DeflateTables.LengthBase[baseCode]), extraBitsLen);

                var distCode = DeflateTables.GetDistanceCode(dist);
                WriteBits(dCodes[distCode], dLengths[distCode]);

                var distExtraBits = DeflateTables.DistanceExtraBits[distCode];
                if (distExtraBits > 0)
                    WriteBits((uint)(dist - DeflateTables.DistanceBase[distCode]), distExtraBits);
            }
        }
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
