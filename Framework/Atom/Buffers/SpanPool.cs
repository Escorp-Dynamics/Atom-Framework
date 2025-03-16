#pragma warning disable CA1000

using System.Buffers;

namespace Atom.Buffers;

/// <summary>
/// Представляет пул для <see cref="Span{T}"/>.
/// </summary>
/// <typeparam name="T">Тип элементов.</typeparam>
public class SpanPool<T> : IDisposable
{
    private const int DefaultPoolSize = 1024;

    private readonly int poolSize;
    private readonly ArrayPool<T> pool;
    private readonly T[][] rented;
    private volatile int rentedCount = -1;
    private bool isDisposed;

    /// <summary>
    /// Общий доступ к <see cref="SpanPool{T}"/>.
    /// </summary>
    public static SpanPool<T> Shared { get; } = new();

    private SpanPool(ArrayPool<T> arrayPool, int poolSize)
    {
        this.poolSize = poolSize;
        pool = arrayPool;
        rented = ArrayPool<T[]>.Shared.Rent(poolSize);
    }

    private SpanPool(ArrayPool<T> arrayPool) : this(arrayPool, DefaultPoolSize) { }

    private SpanPool(int poolSize) : this(ArrayPool<T>.Shared, poolSize) { }

    private SpanPool(int maxArrayLength, int maxArraysPerBucket) : this(ArrayPool<T>.Create(maxArrayLength, maxArraysPerBucket)) { }

    private SpanPool() : this(ArrayPool<T>.Shared) { }

    /// <summary>
    /// Арендует сегмент памяти из пула.
    /// </summary>
    /// <param name="minimumLength">Минимальная длина сегмента памяти.</param>
    /// <returns>Арендованный сегмент памяти.</returns>
    public virtual Span<T> Rent(int minimumLength)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumLength);

        var index = Interlocked.Increment(ref rentedCount);
        if (index >= poolSize) throw new InvalidOperationException("Пул переполнен");

        rented[index] = pool.Rent(minimumLength);
        return rented[index].AsSpan(0, minimumLength);
    }

    /// <summary>
    /// Возвращает арендованный сегмент памяти в пул.
    /// </summary>
    /// <param name="span">Арендованный сегмент памяти.</param>
    /// <param name="clearArray">Значение, указывающее, нужно ли очистить массив перед возвратом.</param>
    public virtual void Return(ReadOnlySpan<T> span, bool clearArray)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
        if (span.IsEmpty) return;

        var count = Interlocked.Decrement(ref rentedCount) + 1;

        if (count < 0)
        {
            Interlocked.Increment(ref rentedCount);
            return;
        }

        var index = -1;

        for (var i = 0; i <= count; ++i)
        {
            if (span.SequenceEqual(rented[i].AsSpan(0, span.Length)))
            {
                index = i;
                break;
            }
        }

        if (index is -1) throw new InvalidDataException("Сегмент памяти не был арендован в этом пуле");
        pool.Return(rented[index], clearArray);
    }

    /// <summary>
    /// Возвращает арендованный сегмент памяти в пул.
    /// </summary>
    /// <param name="span">Арендованный сегмент памяти.</param>
    public void Return(ReadOnlySpan<T> span) => Return(span, default);

    /// <summary>
    /// Создает новый пул сегментов памяти с указанными ограничениями.
    /// </summary>
    /// <param name="maxArrayLength">Максимальная длина массива.</param>
    /// <param name="maxArraysPerBucket">Максимальное количество массивов в каждом бакете.</param>
    /// <returns>Новый пул сегментов памяти.</returns>
    public static SpanPool<T> Create(int maxArrayLength, int maxArraysPerBucket) => new(maxArrayLength, maxArraysPerBucket);

    /// <summary>
    /// Создает новый пул сегментов памяти с указанными ограничениями.
    /// </summary>
    /// <param name="poolSize">Размер пула.</param>
    /// <returns>Новый пул сегментов памяти.</returns>
    public static SpanPool<T> Create(int poolSize) => new(poolSize);

    /// <summary>
    /// Создает новый пул сегментов памяти.
    /// </summary>
    public static SpanPool<T> Create() => new();

    /// <summary>
    /// Освобождает ресурсы, используемые объектом.
    /// </summary>
    /// <param name="disposing">Значение, указывающее, вызывается ли метод из конструктора или из финализатора.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;

        if (disposing)
        {
            while (rentedCount is not -1)
            {
                pool.Return(rented[rentedCount], true);
                Interlocked.Decrement(ref rentedCount);
            }

            ArrayPool<T[]>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Освобождает все ресурсы, используемые объектом.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}