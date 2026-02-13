#pragma warning disable CA1000, MA0018

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Atom.Buffers;

/// <summary>
/// Представляет средство буферизации объектов.
/// </summary>
/// <typeparam name="T">Тип объекта.</typeparam>
public unsafe class ObjectPool<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T> : IDisposable
{
    [StructLayout(LayoutKind.Auto)]
    private struct CacheLine
    {
        public T Value;
        public int OwnerThreadId;
    }

    private const int DefaultCapacity = 1 << 20;
    private const int BatchSize = 64;

    private readonly CacheLine[] globalBuffer;
    private int globalIndex;
    private readonly int mask;
    private readonly Func<T> factory;
    private readonly ThreadLocal<T[]?> threadBuffer;
    private readonly ThreadLocal<int> threadIndex;

    private bool isDisposed;

    /// <summary>
    /// Общий доступ к <see cref="ObjectPool{T}"/>.
    /// </summary>
    public static ObjectPool<T> Shared { get; } = Create();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ObjectPool(Func<T>? factory, int capacity)
    {
        capacity = (int)BitOperations.RoundUpToPowerOf2((uint)capacity);
        globalBuffer = GC.AllocateArray<CacheLine>(capacity, pinned: true);
        mask = capacity - 1;
        this.factory = factory ?? CreateDefaultFactory();
        threadBuffer = new ThreadLocal<T[]?>(() => null, trackAllValues: false);
        threadIndex = new ThreadLocal<int>(() => 0, trackAllValues: false);
    }

    /// <summary>
    /// Освобождает ресурсы, используемые объектом.
    /// </summary>
    /// <param name="disposing">Значение, указывающее, освобождать ли управляемые ресурсы.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;
        if (Interlocked.Exchange(ref isDisposed, value: true)) return;

        Array.Clear(globalBuffer);
        threadBuffer.Dispose();
        threadIndex.Dispose();
    }

    /// <summary>
    /// Арендует объект из пула.
    /// </summary>
    /// <returns>Арендованный объект.</returns>
    /// <exception cref="ObjectDisposedException">Пул был освобождён.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual T Rent(Func<T> itemFactory)
    {
        ArgumentNullException.ThrowIfNull(itemFactory);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        var buffer = threadBuffer.Value;
        var count = threadIndex.Value;

        if (buffer is not null && count > 0)
        {
            count--;
            threadIndex.Value = count;
            return buffer[count];
        }

        var newIndex = Interlocked.Add(ref globalIndex, -BatchSize);
        var startIndex = (newIndex + BatchSize) & mask;

        for (var i = 0; i < BatchSize; ++i)
        {
            var idx = (startIndex + i) & mask;
            ref var slot = ref globalBuffer[idx];

            if (Volatile.Read(ref slot.OwnerThreadId) is 0)
            {
                Interlocked.Exchange(ref slot.Value, itemFactory());
                Volatile.Write(ref slot.OwnerThreadId, Environment.CurrentManagedThreadId);
            }
        }

        buffer = new T[BatchSize];
        threadBuffer.Value = buffer;

        for (var i = 0; i < BatchSize; ++i)
        {
            buffer[i] = globalBuffer[(startIndex + i) & mask].Value;
        }

        const int initialCount = BatchSize;
        threadIndex.Value = initialCount;
        var nextCount = initialCount - 1;
        threadIndex.Value = nextCount;
        return buffer[nextCount];
    }

    /// <summary>
    /// Арендует объект из пула.
    /// </summary>
    /// <returns>Арендованный объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual T Rent() => Rent(factory);

    /// <summary>
    /// Возвращает объект в пул.
    /// </summary>
    /// <param name="item">Возвращаемый объект.</param>
    /// <exception cref="ObjectDisposedException">Пул был освобождён.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Return(T item)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        var buffer = threadBuffer.Value;
        var count = threadIndex.Value;

        if (buffer is not null && count < buffer.Length)
        {
            buffer[count] = item;
            threadIndex.Value = count + 1;
            return;
        }

        while (true)
        {
            var idx = Interlocked.Increment(ref globalIndex) & mask;
            ref var slot = ref globalBuffer[idx];

            if (Volatile.Read(ref slot.OwnerThreadId) == Environment.CurrentManagedThreadId)
            {
                Interlocked.Exchange(ref slot.Value, item);
                return;
            }

            if (Interlocked.CompareExchange(ref slot.OwnerThreadId, Environment.CurrentManagedThreadId, 0) is 0)
            {
                slot.Value = item;
                return;
            }
        }
    }

    /// <summary>
    /// Возвращает арендованный объект в пул.
    /// </summary>
    /// <param name="obj">Арендованный объект.</param>
    /// <param name="resetter">Метод сброса свойств у объекта.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Return(T obj, Action<T> resetter)
    {
        resetter?.Invoke(obj);
        Return(obj);
    }

    /// <summary>
    /// Освобождает все ресурсы, используемые объектом.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<T> CreateDefaultFactory()
    {
        if (typeof(T).IsValueType) return () => default!;
        var ctor = typeof(T).GetConstructor(Type.EmptyTypes) ?? throw new InvalidOperationException($"Тип {typeof(T)} не содержит конструктор по умолчанию");
        return Expression.Lambda<Func<T>>(Expression.New(ctor)).Compile(preferInterpretation: true);
    }

    /// <summary>
    /// Создает новый пул объектов.
    /// </summary>
    /// <param name="factory">Функция возврата значения из пула, если он пуст.</param>
    /// <param name="capacity">Размер пула.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ObjectPool<T> Create(Func<T> factory, int capacity)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new(factory, capacity);
    }

    /// <summary>
    /// Создает новый пул объектов.
    /// </summary>
    /// <param name="factory">Функция возврата значения из пула, если он пуст.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ObjectPool<T> Create(Func<T> factory) => Create(factory, DefaultCapacity);

    /// <summary>
    /// Создает новый пул объектов.
    /// </summary>
    /// <param name="capacity">Размер пула.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ObjectPool<T> Create(int capacity) => new(default, capacity);

    /// <summary>
    /// Создает новый пул объектов.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ObjectPool<T> Create() => Create(DefaultCapacity);
}
