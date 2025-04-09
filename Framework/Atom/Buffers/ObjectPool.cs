#pragma warning disable CA1000, IDE0044, IDE0051

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Atom.Buffers;

/// <summary>
/// Представляет средство буферизации объектов.
/// </summary>
/// <typeparam name="T">Тип объекта.</typeparam>
public unsafe class ObjectPool<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T> : IDisposable
{
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

    private bool isDisposed;

    private static readonly Lazy<Func<T>> defaultFactory = new(GetDefaultFactory, true);

    [ThreadStatic]
    private static T[]? threadBuffer;

    [ThreadStatic]
    private static int threadIndex;

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
        this.factory = factory ?? defaultFactory.Value;
    }

    /// <summary>
    /// Освобождает ресурсы, используемые объектом.
    /// </summary>
    /// <param name="disposing">Значение, указывающее, освобождать ли управляемые ресурсы.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void Dispose(bool disposing)
    {
        if (!Interlocked.CompareExchange(ref isDisposed, true, default)) Array.Clear(globalBuffer);
    }

    /// <summary>
    /// Арендует объект из пула.
    /// </summary>
    /// <returns>Арендованный объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual T Rent([NotNull] Func<T> factory)
    {
        var buffer = Volatile.Read(ref threadBuffer);

        if (buffer is not null && Volatile.Read(ref threadIndex) > 0) return buffer[Interlocked.Decrement(ref threadIndex)];

        var newIndex = Interlocked.Add(ref globalIndex, -BatchSize);
        var startIndex = (newIndex + BatchSize) & mask;

        for (var i = 0; i < BatchSize; ++i)
        {
            var idx = (startIndex + i) & mask;
            ref var slot = ref globalBuffer[idx];

            if (Volatile.Read(ref slot.OwnerThreadId) is 0)
            {
                Interlocked.Exchange(ref slot.Value, factory());
                Volatile.Write(ref slot.OwnerThreadId, Environment.CurrentManagedThreadId);
            }
        }

        buffer = new T[BatchSize];
        Volatile.Write(ref threadBuffer, buffer);

        for (var i = 0; i < BatchSize; ++i) buffer[i] = globalBuffer[(startIndex + i) & mask].Value;

        Volatile.Write(ref threadIndex, BatchSize);
        return buffer[Interlocked.Decrement(ref threadIndex)];
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Return(T item)
    {
        var buffer = Volatile.Read(ref threadBuffer);

        if (buffer is not null && Volatile.Read(ref threadIndex) < buffer.Length)
        {
            var idx = Interlocked.Increment(ref threadIndex) - 1;
            buffer[idx] = item;
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
    private static Func<T> GetDefaultFactory()
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
    public static ObjectPool<T> Create([NotNull] Func<T> factory, int capacity) => new(factory, capacity);

    /// <summary>
    /// Создает новый пул объектов.
    /// </summary>
    /// <param name="factory">Функция возврата значения из пула, если он пуст.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ObjectPool<T> Create([NotNull] Func<T> factory) => Create(factory, DefaultCapacity);

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