#pragma warning disable CA2225

using System.Buffers;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Atom.Collections;

/// <summary>
/// Представляет оптимизированный массив фиксированного размера, который итерирует лишь назначенные элементы.
/// </summary>
/// <typeparam name="T">Тип элементов массива.</typeparam>
public class SparseArray<T> : IEnumerable<T>
{
    private readonly bool isExternal;
    private readonly T[] values;
    private readonly int[] indexes;

    private int currentIndex = -1;
    private static readonly ArrayPool<int> indexPool = ArrayPool<int>.Create();

    /// <summary>
    /// Задаёт или возвращает значение массива по его индексу.
    /// </summary>
    /// <value>Значение в массиве.</value>
    public T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ValidateIndex(index);
            return values[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => AddOrUpdate(index, value);
    }

    /// <summary>
    /// Длина массива.
    /// </summary>
    public int Length => values.Length;

    /// <summary>
    /// Текущий индекс.
    /// </summary>
    public int CurrentIndex => Volatile.Read(ref Unsafe.AsRef(in currentIndex));

    /// <summary>
    /// Определяет, является ли массив пустым.
    /// </summary>
    public bool IsEmpty => CurrentIndex is -1;

    /// <summary>
    /// Определяет, были ли ресурсы высвобождены.
    /// </summary>
    public bool IsReleased { get; private set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SparseArray{T}"/>.
    /// </summary>
    /// <param name="array">Исходный массив.</param>
    public SparseArray(T[] array)
    {
        isExternal = true;
        values = array;
        indexes = indexPool.Rent(values.Length);
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SparseArray{T}"/>.
    /// </summary>
    /// <param name="capacity">Ёмкость массива.</param>
    public SparseArray(int capacity) : this(ArrayPool<T>.Shared.Rent(capacity)) => isExternal = default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateIndex(int index)
    {
        if (IsReleased) throw new InvalidOperationException("Ресурсы были высвобождены");
        if (index < 0 || index >= values.Length) throw new ArgumentOutOfRangeException(nameof(index));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe bool ContainsIndex(int snapshot, int index)
    {
        if (snapshot < 0) return false;

        var currentIndexes = indexes.AsSpan(0, snapshot + 1);
        fixed (int* ptr = currentIndexes)
        {
            for (var p = ptr; p < ptr + currentIndexes.Length; ++p)
            {
                if (Volatile.Read(ref *p) == index) return true;
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryAddIndex(int index, out int slot)
    {
        var current = Volatile.Read(ref currentIndex);
        var newSlot = current + 1;

        if (newSlot >= indexes.Length)
        {
            slot = -1;
            return false;
        }

        if (Interlocked.CompareExchange(ref currentIndex, newSlot, current) == current)
        {
            Volatile.Write(ref indexes[newSlot], index);
            slot = newSlot;
            return true;
        }

        slot = -1;
        return false;
    }

    /// <summary>
    /// Добавляет или обновляет значение массива по его индексу.
    /// </summary>
    /// <param name="index">Индекс элемента.</param>
    /// <param name="value">Значение элемента.</param>
    public void AddOrUpdate(int index, T value)
    {
        ValidateIndex(index);
        values[index] = value;

        if (ContainsIndex(Volatile.Read(ref currentIndex), index)) return;

        var spinWait = new SpinWait();
        while (true)
        {
            if (TryAddIndex(index, out _)) return;

            spinWait.SpinOnce();

            if (ContainsIndex(Volatile.Read(ref currentIndex), index)) return;
        }
    }

    /// <summary>
    /// Добавляет значения в конец массива.
    /// </summary>
    /// <param name="values">Добавляемые значения.</param>
    public void AddRange([NotNull] params IEnumerable<T> values)
    {
        foreach (var value in values) Add(value);
    }

    /// <summary>
    /// Добавляет значение в конец массива.
    /// </summary>
    /// <param name="value">Значение массива.</param>
    public void Add(T value)
    {
        if (IsReleased) throw new InvalidOperationException("Ресурсы были высвобождены");

        var spinWait = new SpinWait();
        while (true)
        {
            var current = Volatile.Read(ref currentIndex);
            var newIdx = current + 1;

            if (newIdx >= indexes.Length || newIdx >= values.Length)
                throw new InvalidOperationException("Массив переполнен");

            var valueIndex = FindAvailableValueIndex(current);

            if (Interlocked.CompareExchange(ref currentIndex, newIdx, current) == current)
            {
                this.values[valueIndex] = value;
                Volatile.Write(ref indexes[newIdx], valueIndex);
                return;
            }

            spinWait.SpinOnce();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindAvailableValueIndex(int current)
    {
        if (current < 0) return 0;

        var currentIndexes = indexes.AsSpan(0, current + 1);
        for (var i = 0; i < values.Length; ++i)
        {
            if (!IsIndexUsed(currentIndexes, i)) return i;
        }

        return current + 1;
    }

    /// <summary>
    /// Возвращает все установленные индексы.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> GetIndexes() => indexes.AsSpan(0, CurrentIndex + 1);

    /// <summary>
    /// Сбрасывает позицию индексов в начало.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset() => Interlocked.Exchange(ref currentIndex, -1);

    /// <summary>
    /// Освобождает используемую память.
    /// </summary>
    /// <param name="clearArray">Указывает, следует ли очищать массив (игнорируется если был использован внешний источник).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Release(bool clearArray)
    {
        if (IsReleased) return;
        IsReleased = true;

        if (!isExternal) ArrayPool<T>.Shared.Return(values, clearArray);
        indexPool.Return(indexes, clearArray: true);
    }

    /// <summary>
    /// Освобождает используемую память.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Release() => Release(default);

    /// <summary>
    /// Возвращает енумератор для перебора только установленных индексов массива.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerator<T> GetEnumerator()
    {
        var idx = CurrentIndex;
        if (idx < 0) yield break;
        for (var i = 0; i <= idx; ++i) yield return values[indexes[i]];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe bool IsIndexUsed(ReadOnlySpan<int> currentIndexes, int index)
    {
        fixed (int* ptr = currentIndexes)
        {
            for (var p = ptr; p < ptr + currentIndexes.Length; ++p)
            {
                if (*p == index) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Явное преобразование из массива в <see cref="SparseArray{T}"/>.
    /// </summary>
    /// <param name="array">Исходный массив.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SparseArray<T>(T[] array) => new(array);
}
