#pragma warning disable CA2225

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Atom.Buffers;

namespace Atom.Collections;

/// <summary>
/// Представляет оптимизированный массив фиксированного размера, который итерирует лишь назначенные элементы.
/// </summary>
/// <typeparam name="T">Тип элементов массива.</typeparam>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="SparseSpan{T}"/>.
/// </remarks>
/// <param name="array">Исходный массив.</param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
[StructLayout(LayoutKind.Auto)]
public ref struct SparseSpan<T>(Span<T> array)
{
    private const int DefaultLength = 1024;

    private readonly bool isExternal = true;
    private readonly Span<T> values = array;
    private readonly Span<int> indexes = StaticPools.SparseSpanIndexPool.Rent(array.Length);

    private volatile int currentIndex = -1;

    /// <summary>
    /// Задаёт или возвращает значение массива по его индексу.
    /// </summary>
    /// <value>Значение в массиве.</value>
    public T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get
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
    public readonly int Length => values.Length;

    /// <summary>
    /// Текущий индекс.
    /// </summary>
    public readonly int CurrentIndex => currentIndex;

    /// <summary>
    /// Определяет, является ли массив пустым.
    /// </summary>
    public readonly bool IsEmpty => currentIndex is -1;

    /// <summary>
    /// Определяет, были ли ресурсы высвобождены.
    /// </summary>
    public bool IsReleased { get; private set; }

    /// <summary>
    /// Возвращает активные индексы.
    /// </summary>
    /// <value></value>
    public readonly ReadOnlySpan<int> Indexes
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => IsReleased ? throw new InvalidOperationException("Ресурсы были высвобождены") : (ReadOnlySpan<int>)indexes[..(currentIndex + 1)];
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SparseSpan{T}"/>.
    /// </summary>
    /// <param name="capacity">Ёмкость массива.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SparseSpan(int capacity) : this(SpanPool<T>.Shared.Rent(capacity)) => isExternal = default;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SparseSpan{T}"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SparseSpan() : this(DefaultLength) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly void ValidateIndex(int index)
    {
        if (IsReleased) throw new InvalidOperationException("Ресурсы были высвобождены");
        if (index < 0 || index >= values.Length) throw new ArgumentOutOfRangeException(nameof(index));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly int FindAvailableIndex(ReadOnlySpan<int> currentIndexes)
    {
        for (var i = 0; i < values.Length; ++i)
        {
            if (!IsIndexUsed(currentIndexes, i))
                return i;
        }

        return currentIndexes[^1] + 1;
    }

    /// <summary>
    /// Добавляет или обновляет значение массива по его индексу.
    /// </summary>
    /// <param name="index">Индекс элемента.</param>
    /// <param name="value">Значение элемента.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void AddOrUpdate(int index, T value)
    {
        ValidateIndex(index);

        var needUpdateIndex = true;

        if (currentIndex >= 0)
        {
            var currentIndexes = Indexes;

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
            var idx = Interlocked.Increment(ref currentIndex);
            indexes[idx] = index;
        }

        values[index] = value;
    }

    /// <summary>
    /// Добавляет значения в конец массива.
    /// </summary>
    /// <param name="values">Добавляемые значения.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void AddRange([NotNull] params IEnumerable<T> values)
    {
        var currentIndexes = Indexes;

        foreach (var value in values)
        {
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
    /// Сбрасывает состояние массива для повторного использования.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        if (IsReleased) throw new InvalidOperationException("Ресурсы были высвобождены");
        Interlocked.Exchange(ref currentIndex, -1);
    }

    /// <summary>
    /// Освобождает используемую память.
    /// </summary>
    /// <param name="clearArray">Указывает, следует ли очищать массив (игнорируется если был использован внешний источник).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Release(bool clearArray)
    {
        if (IsReleased) return;
        IsReleased = true;

        if (!isExternal) SpanPool<T>.Shared.Return(values, clearArray);
        StaticPools.SparseSpanIndexPool.Return(indexes);
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
    public readonly SparseEnumerator<T> GetEnumerator() => new(values, indexes, currentIndex);

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
    /// Явное преобразование из диапазона в <see cref="SparseSpan{T}"/>.
    /// </summary>
    /// <param name="span">Исходный диапазон.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator SparseSpan<T>(Span<T> span) => new(span);
}