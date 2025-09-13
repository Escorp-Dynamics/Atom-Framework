using System.Buffers;
using System.Runtime.CompilerServices;

namespace Atom.Net.Https.Headers.HPack;

internal sealed class HPackDynamicTable(int capacity)
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

    private Entry[] entries = new Entry[16];  // кольцевой буфер
    private int head;         // индекс следующей записи для вставки (в начало)
    private int capacity = capacity;     // лимит (байт)

    private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;

    public int Count { get; private set; }

    /// <summary>
    /// Получить запись по позиции 1..Count (1 — самая свежая, MRU).
    /// Возвращаем TableEntry по значению; внутри — спаны на внутренние массивы.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TableEntry Get(int oneBasedPosition)
    {
        if ((uint)oneBasedPosition is 0 || oneBasedPosition > Count)
            throw new ArgumentOutOfRangeException(nameof(oneBasedPosition));

        // Позиция 1 → индекс головы (_head), 2 → следующий и т.д. (кольцевой буфер степени двойки).
        var idx = (head + (oneBasedPosition - 1)) & (entries.Length - 1);
        ref readonly var e = ref entries[idx];

        // Возвращаем "тонкий" TableEntry из спанов (без аллокаций).
        return new TableEntry(
            new ReadOnlySpan<byte>(e.Name, 0, e.NameLen),
            new ReadOnlySpan<byte>(e.Value, 0, e.ValueLen)
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetCapacity(int newCapacity)
    {
        capacity = newCapacity;
        EvictToCapacity();
    }

    public int Size { get; private set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(ReadOnlySpan<char> name, ReadOnlySpan<char> value)
    {
        var nb = RentCopy(name);
        var vb = RentCopy(value);
        Insert(nb, name.Length, vb, value.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
        var nb = RentCopy(name);
        var vb = RentCopy(value);
        Insert(nb, name.Length, vb, value.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EvictToCapacity()
    {
        while (Size > capacity && Count > 0)
        {
            var tail = (head + Count - 1) & (entries.Length - 1);
            ReturnToPool(in entries[tail]);
            var sz = entries[tail].Size;
            entries[tail] = default;
            Count--;
            Size -= sz;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        for (var i = 0; i < entries.Length; i++)
        {
            if (entries[i].Name == null && entries[i].Value == null) continue;
            ReturnToPool(in entries[i]);
            entries[i] = default;
        }
        head = 0; Count = 0; Size = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Grow()
    {
        // Степень двойки для простых &-операций.
        var newLen = entries.Length << 1;
        var arr = new Entry[newLen];

        // Копируем логически отсортированные записи 0.._count-1 в начало.
        for (var i = 0; i < Count; i++)
            arr[i] = entries[(head + i) & (entries.Length - 1)];

        entries = arr;
        head = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Insert(byte[] name, int nameLen, byte[] value, int valueLen)
    {
        var e = new Entry { Name = name, NameLen = nameLen, Value = value, ValueLen = valueLen };
        var sz = e.Size;

        if (sz > capacity)
        {
            Clear();
            return;
        }

        if (Count == entries.Length) Grow();

        var ins = (head - 1) & (entries.Length - 1);
        entries[ins] = e;
        head = ins;

        if (Count < entries.Length) Count++;
        Size += sz;
        EvictToCapacity();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReturnToPool(in Entry e)
    {
        if (e.Name is not null) Pool.Return(e.Name);
        if (e.Value is not null) Pool.Return(e.Value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] RentCopy(ReadOnlySpan<byte> s)
    {
        var a = Pool.Rent(s.Length);
        s.CopyTo(a);
        return a; // фактическая длина хранится в NameLen/ValueLen
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] RentCopy(ReadOnlySpan<char> s)
    {
        var a = Pool.Rent(s.Length);

        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            a[i] = (byte)(c <= 0x7Fu ? c : 0x3Fu);
        }

        return a;
    }
}