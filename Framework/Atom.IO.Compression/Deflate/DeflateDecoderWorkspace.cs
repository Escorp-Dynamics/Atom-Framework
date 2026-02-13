#pragma warning disable CA1819, CA1051, IDE0032, S1450, S2325, CA1822, MA0041, MA0038, MA0051, S3776, IDE0078

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Atom.IO.Compression.Deflate;

/// <summary>
/// Рабочее пространство декодера Deflate. Единый pinned буфер для всех данных.
/// Исключает повторные аллокации и bounds checking.
/// </summary>
internal sealed unsafe class DeflateDecoderWorkspace : IDisposable
{
    #region Constants

    // Input buffer для чтения из stream
    private const int InputSize = 64 * 1024;

    // Unified buffer: 32KB история + 32KB output
    private const int WindowSize = 32 * 1024;
    private const int UnifiedSize = WindowSize * 2;

    // Fixed Huffman таблицы (предкомпилированные)
    // Lit/Len: 9-bit = 512 entries × 4 bytes (packed) = 2KB
    private const int FixedLitLenLog = 9;
    private const int FixedLitLenSize = 1 << FixedLitLenLog;
    private const int FixedLitLenBytes = FixedLitLenSize * sizeof(uint);

    // Distance: 5-bit = 32 entries × 4 bytes = 128B
    private const int FixedDistLog = 5;
    private const int FixedDistSize = 1 << FixedDistLog;
    private const int FixedDistBytes = FixedDistSize * sizeof(uint);

    // Dynamic Huffman таблицы
    // Lit/Len: 11-bit = 2048 entries × 4 bytes = 8KB (помещается в L1)
    private const int DynLitLenLog = 11;
    private const int DynLitLenSize = 1 << DynLitLenLog;
    private const int DynLitLenBytes = DynLitLenSize * sizeof(uint);

    // Distance: 11-bit = 2048 entries × 4 bytes = 8KB
    private const int DynDistLog = 11;
    private const int DynDistSize = 1 << DynDistLog;
    private const int DynDistBytes = DynDistSize * sizeof(uint);

    // Code lengths для dynamic Huffman (временный буфер)
    // hlit может быть до 288 (257 + 31), hdist до 32 (1 + 31), итого 320
    private const int MaxCodes = 288 + 32; // lit/len + dist
    private const int CodeLengthsBytes = MaxCodes * sizeof(byte);

    // Выравнивание для SIMD
    private const int Alignment = 64;

    #endregion

    #region Layout

    // Смещения в едином буфере
    private const int InputOffset = 0;
    private static readonly int UnifiedOffset = Align(InputOffset + InputSize);
    private static readonly int FixedLitLenOffset = Align(UnifiedOffset + UnifiedSize);
    private static readonly int FixedDistOffset = Align(FixedLitLenOffset + FixedLitLenBytes);
    private static readonly int DynLitLenOffset = Align(FixedDistOffset + FixedDistBytes);
    private static readonly int DynDistOffset = Align(DynLitLenOffset + DynLitLenBytes);
    private static readonly int CodeLengthsOffset = Align(DynDistOffset + DynDistBytes);
    private static readonly int TotalSize = Align(CodeLengthsOffset + CodeLengthsBytes);

    private static int Align(int value) => (value + Alignment - 1) & ~(Alignment - 1);

    #endregion

    #region Fields

    private readonly byte[] buffer;
    private readonly byte* basePtr;
    private bool disposed;

    #endregion

    #region Constructor

    public DeflateDecoderWorkspace()
    {
        // Единый pinned буфер
        buffer = GC.AllocateUninitializedArray<byte>(TotalSize, pinned: true);
        basePtr = (byte*)Unsafe.AsPointer(ref buffer[0]);

        // Строим Fixed Huffman таблицы один раз
        BuildFixedHuffmanTables();
    }

    #endregion

    #region Pointers — прямой доступ без bounds checking

    /// <summary>Input buffer pointer. Size: 64KB.</summary>
    public byte* InputPtr
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => basePtr + InputOffset;
    }

    /// <summary>Input buffer size.</summary>
    public int InputCapacity => InputSize;

    /// <summary>Unified buffer pointer (history + output). Size: 64KB.</summary>
    public byte* UnifiedPtr
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => basePtr + UnifiedOffset;
    }

    /// <summary>Unified buffer size.</summary>
    public int UnifiedCapacity => UnifiedSize;

    /// <summary>Window size (32KB).</summary>
    public int WindowSizeValue => WindowSize;

    /// <summary>Fixed Lit/Len Huffman packed table. Size: 512 entries.</summary>
    public uint* FixedLitLenPtr
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (uint*)(basePtr + FixedLitLenOffset);
    }

    /// <summary>Fixed Lit/Len table log (9).</summary>
    public int FixedLitLenTableLog => FixedLitLenLog;

    /// <summary>Fixed Lit/Len table mask.</summary>
    public int FixedLitLenMask => FixedLitLenSize - 1;

    /// <summary>Fixed Distance Huffman packed table. Size: 32 entries.</summary>
    public uint* FixedDistPtr
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (uint*)(basePtr + FixedDistOffset);
    }

    /// <summary>Fixed Distance table log (5).</summary>
    public int FixedDistTableLog => FixedDistLog;

    /// <summary>Fixed Distance table mask.</summary>
    public int FixedDistMask => FixedDistSize - 1;

    /// <summary>Dynamic Lit/Len Huffman packed table. Size: 32768 entries.</summary>
    public uint* DynLitLenPtr
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (uint*)(basePtr + DynLitLenOffset);
    }

    /// <summary>Dynamic Lit/Len table max size.</summary>
    public int DynLitLenMaxSize => DynLitLenSize;

    /// <summary>Dynamic Distance Huffman packed table. Size: 32768 entries.</summary>
    public uint* DynDistPtr
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (uint*)(basePtr + DynDistOffset);
    }

    /// <summary>Dynamic Distance table max size.</summary>
    public int DynDistMaxSize => DynDistSize;

    /// <summary>Временный буфер для code lengths. Size: 318 bytes.</summary>
    public byte* CodeLengthsPtr
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => basePtr + CodeLengthsOffset;
    }

    #endregion

    #region Fixed Huffman Tables

    private void BuildFixedHuffmanTables()
    {
        // Используем DeflateTables для получения code lengths
        // lit/len таблица: defaultSymbol = 256 (EOB)
        BuildPackedTable(DeflateTables.FixedLitLenCodeLengths, FixedLitLenPtr, FixedLitLenLog, 256);
        // distance таблица: defaultSymbol = 0
        BuildPackedTable(DeflateTables.FixedDistCodeLengths, FixedDistPtr, FixedDistLog, 0);
    }

    /// <summary>
    /// Строит packed таблицу из code lengths: формат (symbol shl 8) | length.
    /// </summary>
    private static void BuildPackedTable(ReadOnlySpan<byte> codeLengths, uint* table, int tableLog, int defaultSymbol)
    {
        var tableSize = 1 << tableLog;
        var maxBits = tableLog;

        // Подсчёт символов каждой длины
        Span<int> blCount = stackalloc int[maxBits + 1];
        blCount.Clear();
        for (var i = 0; i < codeLengths.Length; i++)
        {
            var len = codeLengths[i];
            if (len != 0 && len <= maxBits)
            {
                blCount[len]++;
            }
        }

        // Вычисляем первый код каждой длины (canonical)
        Span<int> nextCode = stackalloc int[maxBits + 1];
        var code = 0;
        for (var bits = 1; bits <= maxBits; bits++)
        {
            code = (code + blCount[bits - 1]) << 1;
            nextCode[bits] = code;
        }

        // Инициализируем таблицу значением по умолчанию
        for (var i = 0; i < tableSize; i++)
        {
            table[i] = ((uint)defaultSymbol << 8) | (uint)maxBits;
        }

        // Заполняем таблицу — все символы
        for (var sym = 0; sym < codeLengths.Length; sym++)
        {
            var len = codeLengths[sym];
            if (len == 0 || len > maxBits)
                continue;

            var c = nextCode[len]++;
            var tableIndex = ReverseBits(c, len);
            var stride = 1 << len;

            // Заполняем все слоты
            for (var idx = tableIndex; idx < tableSize; idx += stride)
            {
                table[idx] = (uint)((sym << 8) | len);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReverseBits(int value, int numBits)
    {
        var result = 0;
        for (var i = 0; i < numBits; i++)
        {
            result = (result << 1) | (value & 1);
            value >>= 1;
        }
        return result;
    }

    #endregion

    #region Dynamic Huffman Helpers

    /// <summary>
    /// Строит packed таблицу из code lengths для Dynamic Huffman.
    /// </summary>
    /// <param name="codeLengths">Длины кодов для каждого символа.</param>
    /// <param name="numSymbols">Количество символов.</param>
    /// <param name="tablePtr">Указатель на packed таблицу.</param>
    /// <param name="tableLog">Логарифм размера таблицы.</param>
    /// <returns>Маска таблицы.</returns>
    public int BuildDynamicTable(ReadOnlySpan<byte> codeLengths, int numSymbols, uint* tablePtr, int tableLog)
    {
        var tableSize = 1 << tableLog;
        var mask = tableSize - 1;

        // Подсчёт количества кодов каждой длины
        Span<int> blCount = stackalloc int[16];
        blCount.Clear();
        for (var i = 0; i < numSymbols; i++)
        {
            if (codeLengths[i] > 0)
                blCount[codeLengths[i]]++;
        }

        // Вычисление начальных кодов для каждой длины
        Span<int> nextCode = stackalloc int[16];
        var code = 0;
        for (var bits = 1; bits <= 15; bits++)
        {
            code = (code + blCount[bits - 1]) << 1;
            nextCode[bits] = code;
        }

        // Генерация таблицы
        for (var sym = 0; sym < numSymbols; sym++)
        {
            var len = codeLengths[sym];
            if (len == 0) continue;

            var huffCode = nextCode[len]++;

            // Заполняем все записи таблицы для этого кода
            var fillBits = tableLog - len;
            if (fillBits >= 0)
            {
                var fillCount = 1 << fillBits;
                var baseIndex = ReverseBits(huffCode, len);

                for (var fill = 0; fill < fillCount; fill++)
                {
                    var index = baseIndex | (fill << len);
                    if (index < tableSize)
                    {
                        tablePtr[index] = (uint)((sym << 8) | len);
                    }
                }
            }
        }

        return mask;
    }

    #endregion

    #region Reset

    /// <summary>
    /// Сбрасывает workspace для повторного использования.
    /// </summary>
    public void Reset()
    {
        // Fixed таблицы не нужно сбрасывать — они статичны
        // Dynamic таблицы будут перезаписаны при следующем использовании
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        // Pinned array освободится при GC
    }

    #endregion
}

/// <summary>
/// Пул workspace'ов для переиспользования.
/// </summary>
internal static class DeflateDecoderWorkspacePool
{
    private static readonly ConcurrentQueue<DeflateDecoderWorkspace> pool = new();

    public static DeflateDecoderWorkspace Rent() =>
        pool.TryDequeue(out var ws) ? ws : new DeflateDecoderWorkspace();

    public static void Return(DeflateDecoderWorkspace ws)
    {
        ws.Reset();
        pool.Enqueue(ws);
    }
}
