#pragma warning disable CA1812

namespace Atom.Net.Http.HPack;

internal sealed class DynamicTable(int maxSize)
{
    private HeaderField[] buffer = [];
    private int insertIndex;
    private int removeIndex;

    public int Count { get; private set; }

    public int Size { get; private set; }

    public int MaxSize { get; private set; } = maxSize;

    public ref readonly HeaderField this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

            index = insertIndex - index - 1;
            if (index < 0) index += buffer.Length;

            return ref buffer[index];
        }
    }

    private void EnsureAvailable(int available)
    {
        while (Count > 0 && MaxSize - Size < available)
        {
            ref var field = ref buffer[removeIndex];
            Size -= field.Length;
            field = default;

            --Count;

            if (++removeIndex == buffer.Length) removeIndex = 0;
        }
    }

    public void Insert(ReadOnlySpan<byte> name, ReadOnlySpan<byte> value) => Insert(staticTableIndex: null, name, value);

    public void Insert(int? staticTableIndex, ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
        var entryLength = HeaderField.GetLength(name.Length, value.Length);
        EnsureAvailable(entryLength);

        if (entryLength > MaxSize) return;

        if (Count == buffer.Length)
        {
            var maxCapacity = MaxSize / HeaderField.RfcOverhead;
            var newBufferSize = Math.Min(Math.Max(16, buffer.Length * 2), maxCapacity);
            var newBuffer = new HeaderField[newBufferSize];

            var headCount = Math.Min(buffer.Length - removeIndex, Count);
            var tailCount = Count - headCount;

            Array.Copy(buffer, removeIndex, newBuffer, 0, headCount);
            Array.Copy(buffer, 0, newBuffer, headCount, tailCount);

            buffer = newBuffer;
            removeIndex = 0;
            insertIndex = Count;
        }

        var entry = new HeaderField(staticTableIndex, name, value);
        buffer[insertIndex] = entry;

        if (++insertIndex == buffer.Length) insertIndex = 0;

        Size += entry.Length;
        Count++;
    }

    public void UpdateMaxSize(int maxSize)
    {
        var previousMax = MaxSize;
        MaxSize = maxSize;

        if (maxSize < previousMax) EnsureAvailable(0);
    }
}