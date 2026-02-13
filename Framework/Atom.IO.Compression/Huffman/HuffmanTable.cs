#pragma warning disable CA1000, CA1815, CA1819, IDE0032, IDE0290

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.IO.Compression.Huffman;

/// <summary>
/// Таблица декодирования Хаффмана с поддержкой fast-path lookup.
/// </summary>
/// <remarks>
/// Оптимизации:
/// - Pointer-based доступ без bounds checking
/// - 2-level lookup: fast (≤ fastBits) + slow (> fastBits)
/// - Кешируемые маски и константы
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly unsafe struct HuffmanTable
{
    #region Constants

    /// <summary>Максимальная длина кода (стандарт для большинства форматов).</summary>
    public const int MaxCodeLength = 16;

    /// <summary>Размер fast-lookup таблицы по умолчанию (256 записей для 8-bit peek).</summary>
    public const int DefaultFastBits = 8;

    /// <summary>Максимальный размер алфавита.</summary>
    public const int MaxSymbols = 288;

    #endregion

    #region Fields

    private readonly byte* symbols;
    private readonly byte* lengths;

    #endregion

    #region Constructors

    /// <summary>
    /// Создаёт таблицу декодирования из предаллоцированных буферов.
    /// </summary>
    /// <param name="tableLog">Логарифм размера таблицы (2^tableLog записей).</param>
    /// <param name="symbolsPtr">Указатель на массив символов.</param>
    /// <param name="lengthsPtr">Указатель на массив длин кодов.</param>
    /// <param name="symbolCount">Количество символов в алфавите.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HuffmanTable(int tableLog, byte* symbolsPtr, byte* lengthsPtr, int symbolCount = MaxSymbols)
    {
        TableLog = tableLog;
        Mask = (1 << tableLog) - 1;
        symbols = symbolsPtr;
        lengths = lengthsPtr;
        SymbolCount = symbolCount;
    }

    #endregion

    #region Properties

    /// <summary>Логарифм размера таблицы.</summary>
    public int TableLog
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    }

    /// <summary>Маска для извлечения индекса из bit peek.</summary>
    public int Mask
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    }

    /// <summary>Размер таблицы (2^TableLog).</summary>
    public int TableSize
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => 1 << TableLog;
    }

    /// <summary>Количество символов в алфавите.</summary>
    public int SymbolCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    }

    /// <summary>Указатель на массив символов (readonly).</summary>
    public ReadOnlySpan<byte> Symbols
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(symbols, TableSize);
    }

    /// <summary>Указатель на массив длин (readonly).</summary>
    public ReadOnlySpan<byte> Lengths
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(lengths, TableSize);
    }

    /// <summary>Признак инициализированной таблицы.</summary>
    public bool IsValid
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => symbols != null && lengths != null && TableLog > 0;
    }

    #endregion

    #region Decoding

    /// <summary>
    /// Декодирует один символ по значению peek (bits).
    /// </summary>
    /// <param name="bits">Значение bit peek (masked).</param>
    /// <param name="consumedBits">Количество потреблённых бит.</param>
    /// <returns>Декодированный символ.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte DecodeSymbol(uint bits, out int consumedBits)
    {
        var index = (int)(bits & (uint)Mask);
        consumedBits = lengths[index];
        return symbols[index];
    }

    /// <summary>
    /// Декодирует один символ (inline версия без out параметра).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte PeekSymbol(uint bits) => symbols[(int)(bits & (uint)Mask)];

    /// <summary>
    /// Возвращает количество бит для символа по peek value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int PeekLength(uint bits) => lengths[(int)(bits & (uint)Mask)];

    /// <summary>
    /// Декодирует символ с прямым pointer-доступом (небезопасно, без проверок).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte DecodeUnsafe(int index, out byte length)
    {
        length = lengths[index];
        return symbols[index];
    }

    #endregion

    #region Batch Decoding

    /// <summary>
    /// Пакетное декодирование нескольких символов.
    /// </summary>
    /// <param name="bitContainer">Битовый контейнер (накопленные биты).</param>
    /// <param name="bitCount">Количество доступных бит.</param>
    /// <param name="output">Выходной буфер.</param>
    /// <param name="maxSymbols">Максимальное количество символов для декодирования.</param>
    /// <returns>Количество декодированных символов и потреблённых бит.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (int symbolsDecoded, int bitsConsumed) DecodeBatch(
        ulong bitContainer,
        int bitCount,
        Span<byte> output,
        int maxSymbols)
    {
        var decoded = 0;
        var consumed = 0;

        fixed (byte* outPtr = output)
        {
            while (decoded < maxSymbols && decoded < output.Length && bitCount >= TableLog)
            {
                var bits = (int)(bitContainer & (uint)Mask);
                var symbol = symbols[bits];
                var length = lengths[bits];

                outPtr[decoded++] = symbol;
                bitContainer >>= length;
                bitCount -= length;
                consumed += length;
            }
        }

        return (decoded, consumed);
    }

    #endregion

    #region Static Factory

    /// <summary>
    /// Создаёт пустую (невалидную) таблицу.
    /// </summary>
    public static HuffmanTable Empty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => default;
    }

    #endregion
}

/// <summary>
/// Managed буфер для HuffmanTable с автоматическим управлением памятью.
/// </summary>
/// <remarks>
/// Используйте для случаев, когда lifetime таблицы известен заранее.
/// Для высокопроизводительных сценариев используйте stackalloc + HuffmanTable напрямую.
/// </remarks>
public sealed class HuffmanTableBuffer : IDisposable
{
    #region Fields

    private readonly byte[] storage;
    private readonly GCHandle handle;
    private bool disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Создаёт буфер для таблицы Хаффмана заданного размера.
    /// </summary>
    /// <param name="tableLog">Логарифм размера таблицы.</param>
    /// <param name="symbolCount">Количество символов в алфавите.</param>
    public HuffmanTableBuffer(int tableLog = 11, int symbolCount = HuffmanTable.MaxSymbols)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tableLog, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tableLog, HuffmanTable.MaxCodeLength);

        TableLog = tableLog;
        SymbolCount = symbolCount;

        var tableSize = 1 << tableLog;
        // symbols + lengths
        storage = new byte[tableSize * 2];
        handle = GCHandle.Alloc(storage, GCHandleType.Pinned);
    }

    #endregion

    #region Properties

    /// <summary>Логарифм размера таблицы.</summary>
    public int TableLog { get; }

    /// <summary>Количество символов в алфавите.</summary>
    public int SymbolCount { get; }

    /// <summary>Размер таблицы.</summary>
    public int TableSize => 1 << TableLog;

    /// <summary>Span для записи символов.</summary>
    public Span<byte> Symbols => storage.AsSpan(0, TableSize);

    /// <summary>Span для записи длин.</summary>
    public Span<byte> Lengths => storage.AsSpan(TableSize, TableSize);

    #endregion

    #region Methods

    /// <summary>
    /// Возвращает готовую таблицу для декодирования.
    /// </summary>
    public unsafe HuffmanTable ToTable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var ptr = (byte*)handle.AddrOfPinnedObject();
        return new HuffmanTable(TableLog, ptr, ptr + TableSize, SymbolCount);
    }

    #endregion

    #region IDisposable

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        if (handle.IsAllocated)
            handle.Free();
    }

    #endregion
}

/// <summary>
/// Таблица декодирования Хаффмана с 16-битными символами (для Deflate и др.).
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly unsafe struct HuffmanTable16
{
    #region Constants

    /// <summary>Максимальный размер алфавита (Deflate: 288 литералов/длин).</summary>
    public const int MaxSymbols = 288;

    #endregion

    #region Fields

    private readonly ushort* symbols;
    private readonly byte* lengths;
    private readonly uint* packed;
    private readonly uint[] packedArray; // Для fixed доступа

    #endregion

    #region Constructors

    /// <summary>
    /// Создаёт таблицу декодирования из предаллоцированных буферов.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HuffmanTable16(int tableLog, ushort* symbolsPtr, byte* lengthsPtr, uint* packedPtr, uint[] packedArr, int symbolCount = MaxSymbols)
    {
        TableLog = tableLog;
        Mask = (1 << tableLog) - 1;
        symbols = symbolsPtr;
        lengths = lengthsPtr;
        packed = packedPtr;
        packedArray = packedArr;
        SymbolCount = symbolCount;
    }

    #endregion

    #region Properties

    /// <summary>Логарифм размера таблицы.</summary>
    public int TableLog { [MethodImpl(MethodImplOptions.AggressiveInlining)] get; }

    /// <summary>Маска для извлечения индекса из bit peek.</summary>
    public int Mask { [MethodImpl(MethodImplOptions.AggressiveInlining)] get; }

    /// <summary>Размер таблицы.</summary>
    public int TableSize { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => 1 << TableLog; }

    /// <summary>Количество символов в алфавите.</summary>
    public int SymbolCount { [MethodImpl(MethodImplOptions.AggressiveInlining)] get; }

    /// <summary>Массив packed таблицы для fixed доступа.</summary>
    public uint[] PackedTable { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => packedArray; }

    #endregion

    #region Methods

    /// <summary>
    /// Декодирует один символ из bit buffer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeSymbol(int peek) => symbols[peek & Mask];

    /// <summary>
    /// Возвращает длину кода для данного peek значения.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetCodeLength(int peek) => lengths[peek & Mask];

    /// <summary>
    /// Декодирует символ и возвращает длину через out параметр.
    /// Оптимизировано для hot path — избегает tuple allocation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeWithLength(int peek, out int codeLength)
    {
        var index = peek & Mask;
        codeLength = lengths[index];
        return symbols[index];
    }

    /// <summary>
    /// Декодирует символ и возвращает его вместе с длиной кода.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (int Symbol, int Length) Decode(int peek)
    {
        var index = peek & Mask;
        return (symbols[index], lengths[index]);
    }

    /// <summary>
    /// Fast decode: одно чтение памяти для symbol и length.
    /// Packed format: (symbol &lt;&lt; 8) | length
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint DecodePacked(int peek) => packed[peek & Mask];

    /// <summary>
    /// Fast decode: одно чтение памяти, возвращает symbol и length.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeFast(int peek, out int codeLength)
    {
        var p = packed[peek & Mask];
        codeLength = (int)(p & 0xFF);
        return (int)(p >> 8);
    }

    #endregion
}

/// <summary>
/// Managed буфер для HuffmanTable16 с 16-битными символами.
/// </summary>
public sealed class HuffmanTableBuffer16 : IDisposable
{
    #region Fields

    private readonly byte[] storage;
    private readonly uint[] packedTable;
    private readonly GCHandle handle;
    private readonly GCHandle packedHandle;
    private bool disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Создаёт буфер для таблицы Хаффмана с 16-битными символами.
    /// </summary>
    /// <param name="tableLog">Логарифм размера таблицы.</param>
    /// <param name="symbolCount">Количество символов в алфавите.</param>
    public HuffmanTableBuffer16(int tableLog = 11, int symbolCount = HuffmanTable16.MaxSymbols)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tableLog, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tableLog, HuffmanTable.MaxCodeLength);

        TableLog = tableLog;
        SymbolCount = symbolCount;

        var tableSize = 1 << tableLog;
        // symbols (ushort) + lengths (byte) + padding
        storage = new byte[(tableSize * 2) + tableSize];
        handle = GCHandle.Alloc(storage, GCHandleType.Pinned);

        // Packed table: (symbol << 8) | length per entry
        packedTable = new uint[tableSize];
        packedHandle = GCHandle.Alloc(packedTable, GCHandleType.Pinned);
    }

    #endregion

    #region Properties

    /// <summary>Логарифм размера таблицы.</summary>
    public int TableLog { get; }

    /// <summary>Количество символов в алфавите.</summary>
    public int SymbolCount { get; }

    /// <summary>Размер таблицы.</summary>
    public int TableSize => 1 << TableLog;

    /// <summary>Span для записи символов (ushort).</summary>
    public Span<ushort> Symbols => MemoryMarshal.Cast<byte, ushort>(storage.AsSpan(0, TableSize * 2));

    /// <summary>Span для записи длин.</summary>
    public Span<byte> Lengths => storage.AsSpan(TableSize * 2, TableSize);

    /// <summary>Span для packed таблицы (symbol shifted left 8 OR length).</summary>
    public Span<uint> Packed => packedTable.AsSpan();

    #endregion

    #region Methods

    /// <summary>
    /// Строит packed таблицу из symbols и lengths.
    /// Вызывать после заполнения Symbols и Lengths.
    /// </summary>
    public unsafe void BuildPackedTable()
    {
        var tableSize = TableSize;
        fixed (byte* storagePtr = storage)
        fixed (uint* packedPtr = packedTable)
        {
            var symPtr = (ushort*)storagePtr;
            var lenPtr = storagePtr + (tableSize * 2);

            for (var i = 0; i < tableSize; i++)
            {
                packedPtr[i] = ((uint)symPtr[i] << 8) | lenPtr[i];
            }
        }
    }

    /// <summary>
    /// Возвращает готовую таблицу для декодирования.
    /// </summary>
    public unsafe HuffmanTable16 ToTable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var ptr = (byte*)handle.AddrOfPinnedObject();
        var packedPtr = (uint*)packedHandle.AddrOfPinnedObject();
        return new HuffmanTable16(TableLog, (ushort*)ptr, ptr + (TableSize * 2), packedPtr, packedTable, SymbolCount);
    }

    #endregion

    #region IDisposable

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        if (handle.IsAllocated)
            handle.Free();

        if (packedHandle.IsAllocated)
            packedHandle.Free();
    }

    #endregion
}
