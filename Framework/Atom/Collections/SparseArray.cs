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

    private volatile int currentIndex = -1;
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
    public int CurrentIndex => currentIndex;

    /// <summary>
    /// Определяет, является ли массив пустым.
    /// </summary>
    public bool IsEmpty => currentIndex is -1;

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
    private int FindAvailableIndex(ReadOnlySpan<int> currentIndexes)
    {
        for (var i = 0; i < values.Length; ++i)
        {
            if (!IsIndexUsed(currentIndexes, i)) return i;
        }

        return currentIndexes[^1] + 1;
    }

    /// <summary>
    /// Добавляет или обновляет значение массива по его индексу.
    /// </summary>
    /// <param name="index">Индекс элемента.</param>
    /// <param name="value">Значение элемента.</param>
    public unsafe void AddOrUpdate(int index, T value)
    {
        ValidateIndex(index);
        var needUpdateIndex = true;

        if (currentIndex >= 0)
        {
            var currentIndexes = GetIndexes();

            fixed (int* ptr = currentIndexes)
            {
                for (var p = ptr; p < ptr + currentIndexes.Length; ++p)
                {
                    if (*p == index)
                    {
                        needUpdateIndex = false;
                        break;
                    }
                }
            }
        }

        if (needUpdateIndex)
        {
            var count = Interlocked.Increment(ref currentIndex);
            indexes[count] = index;
        }

        values[index] = value;
    }

    /// <summary>
    /// Добавляет значения в конец массива.
    /// </summary>
    /// <param name="values">Добавляемые значения.</param>
    public unsafe void AddRange([NotNull] params IEnumerable<T> values)
    {
        foreach (var value in values)
        {
            var currentIndexes = GetIndexes();
            var idx = Interlocked.Increment(ref currentIndex);
            var index = FindAvailableIndex(currentIndexes);

            indexes[idx] = index;
            this.values[index] = value;
        }
    }

    /// <summary>
    /// Добавляет значение в конец массива.
    /// </summary>
    /// <param name="value">Значение массива.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T value) => AddRange(value);

    /// <summary>
    /// Возвращает все установленные индексы.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<int> GetIndexes() => indexes.AsSpan(0, currentIndex + 1);

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
        if (currentIndex < 0) yield break;
        for (var i = 0; i <= currentIndex; ++i) yield return values[indexes[i]];
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
