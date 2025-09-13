using System.Buffers;
using System.Runtime.CompilerServices;

namespace Atom.Net.Https.Headers.QPack;

/// <summary>
/// Динамическая таблица QPACK с абсолютной нумерацией: первый insert имеет absoluteIndex=0.
/// Размер записи: nameLen + valueLen + 32 (RFC 9204 §3.2.1/3.2.2).
/// </summary>
internal sealed class QPackDynamicTable(int capacity)
{
    private struct Entry
    {
        public byte[] Name;
        public int NameLen;
        public byte[] Value;
        public int ValueLen;
        public readonly int Size
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => NameLen + ValueLen + 32;
        }
    }

    private Entry[] entries = new Entry[16]; // кольцевой буфер
    private int head; // позиция для следующей вставки (MRU)
    private int count; // текущее число записей в буфере

    private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;

    /// <summary>Общее число вставок/дубликатов (Insert Count), == 1 + последний абсолютный индекс, иначе 0 если пусто.</summary>
    public int InsertCount { get; private set; }

    /// <summary>Известное полученное число вставок (Known Received Count) — ведётся декодером.</summary>
    public int KnownReceivedCount { get; private set; }

    public int Size { get; private set; }

    /// <summary>
    /// Текущая ёмкость динамической таблицы в байтах (для RIC/MaxEntries).
    /// </summary>
    public int CapacityBytes { get; private set; } = capacity;

    /// <summary>
    /// Максимальное число записей (= capacity/32) по RFC 9204 §3.2.1.
    /// </summary>
    public int MaxEntries => CapacityBytes / 32;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Insert(byte[] name, int nameLen, byte[] value, int valueLen)
    {
        var e = new Entry { Name = name, NameLen = nameLen, Value = value, ValueLen = valueLen };
        var need = e.Size;
        if (need > CapacityBytes) { ClearAll(); return InsertCount - 1; } // запись больше таблицы — очистим по RFC
        EnsureRoom(need);


        if (count == entries.Length) Grow();
        head = (head - 1) & (entries.Length - 1);
        entries[head] = e;
        count++;
        Size += need;
        InsertCount++;
        return InsertCount - 1; // absolute index новой записи
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EvictToCapacity() => EnsureRoom(0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Grow()
    {
        var newLen = entries.Length << 1;
        var arr = new Entry[newLen];
        for (var i = 0; i < count; i++) arr[i] = entries[(head + i) & (entries.Length - 1)];
        entries = arr; head = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureRoom(int need)
    {
        while (Size + need > CapacityBytes && count > 0)
        {
            var tail = (head + count - 1) & (entries.Length - 1);
            Size -= entries[tail].Size;
            ReturnToPool(in entries[tail]);
            entries[tail] = default;
            count--;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ClearAll()
    {
        for (var i = 0; i < entries.Length; i++)
        {
            if (entries[i].Name is null && entries[i].Value is null) continue;
            ReturnToPool(in entries[i]);
            entries[i] = default;
        }

        head = 0;
        count = 0;
        Size = 0;
        InsertCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnInsertCountIncrement(int increment)
    {
        KnownReceivedCount += increment;
        if (KnownReceivedCount < 0) KnownReceivedCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetCapacity(int newCapacity)
    {
        CapacityBytes = newCapacity;
        EvictToCapacity();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Add(scoped ReadOnlySpan<byte> name, scoped ReadOnlySpan<byte> value)
    {
        var nb = Copy(name);
        var vb = Copy(value);
        return Insert(nb, name.Length, vb, value.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Duplicate(int relativeIndex)
    {
        if ((uint)relativeIndex >= (uint)count) throw new ArgumentOutOfRangeException(nameof(relativeIndex));
        var idx = (head + relativeIndex) & (entries.Length - 1);
        ref readonly var e = ref entries[idx];
        return Insert(Copy(e.Name, e.NameLen), e.NameLen, Copy(e.Value, e.ValueLen), e.ValueLen);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TableEntry GetByAbsolute(int absoluteIndex)
    {
        var n = InsertCount; // n = count of inserted (absoluteIndex in [0..n-1])
        var dropped = Math.Max(0, n - count); // сколько «свалилось» из-за эвикции

        ArgumentOutOfRangeException.ThrowIfLessThan((uint)absoluteIndex, (uint)dropped);

        var relFromHead = n - 1 - absoluteIndex; // MRU=0

        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)relFromHead, (uint)count);

        var idx = (head + relFromHead) & (entries.Length - 1);
        ref readonly var e = ref entries[idx];

        return new TableEntry(new ReadOnlySpan<byte>(e.Name, 0, e.NameLen), new ReadOnlySpan<byte>(e.Value, 0, e.ValueLen));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TableEntry GetByRelative_EncoderStream(int relativeIndex)
    {
        // Относительный индекс на encoder stream: 0 — последняя вставка
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)relativeIndex, (uint)count);

        var idx = (head + relativeIndex) & (entries.Length - 1);
        ref readonly var e = ref entries[idx];

        return new TableEntry(new ReadOnlySpan<byte>(e.Name, 0, e.NameLen), new ReadOnlySpan<byte>(e.Value, 0, e.ValueLen));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TableEntry GetByRelative_RepresentationBase(int baseValue, int relativeIndex)
    {
        // В представлениях относительный индекс 0 ссылается на entry с absoluteIndex=Base-1
        var abs = baseValue - relativeIndex - 1;
        return GetByAbsolute(abs);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] Copy(ReadOnlySpan<byte> s)
    {
        var a = Pool.Rent(s.Length);
        s.CopyTo(a);
        return a;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] Copy(byte[] src, int len)
    {
        var a = Pool.Rent(len);
        new ReadOnlySpan<byte>(src, 0, len).CopyTo(a);
        return a;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReturnToPool(in Entry e)
    {
        if (e.Name is not null) Pool.Return(e.Name);
        if (e.Value is not null) Pool.Return(e.Value);
    }
}