#pragma warning disable CA1000

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace Atom.Buffers;

/// <summary>
/// Представляет пул объектов.
/// </summary>
/// <typeparam name="T">Тип объекта.</typeparam>
public class ObjectPool<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T> : IDisposable
{
    private const int DefaultPoolSize = 1024;

    private readonly Func<T> factory;
    private readonly int capacity;
    private readonly T[] pool;

    private volatile int index = -1;
    private bool isDisposed;

    /// <summary>
    /// Общий доступ к <see cref="ObjectPool{T}"/>.
    /// </summary>
    public static ObjectPool<T> Shared { get; } = Create();

    private ObjectPool(Func<T> factory, int capacity)
    {
        this.factory = factory;
        pool = ArrayPool<T>.Shared.Rent(capacity);
        this.capacity = capacity;
    }

    private ObjectPool(int capacity)
    {
        var type = typeof(T);
        var ctor = type.GetConstructor(Type.EmptyTypes) ?? throw new InvalidOperationException($"Тип {type.FullName} не имеет конструктора без параметров.");
        var newExpression = Expression.New(ctor);
        var lambda = Expression.Lambda<Func<T>>(newExpression);

        factory = lambda.Compile();
        pool = ArrayPool<T>.Shared.Rent(capacity);
        this.capacity = capacity;
    }

    /// <summary>
    /// Арендует объект из пула.
    /// </summary>
    /// <returns>Арендованный объект.</returns>
    public virtual T Rent()
    {
        var currentIndex = Interlocked.Decrement(ref index) + 1;
        if (currentIndex >= 0) return pool[currentIndex];

        Interlocked.Increment(ref index);
        return factory();
    }

    /// <summary>
    /// Возвращает арендованный объект в пул.
    /// </summary>
    /// <param name="obj">Арендованный объект.</param>
    /// <param name="resetter">Метод сброса свойств у объекта.</param>
    public virtual void Return(T obj, Action<T> resetter)
    {
        Return(obj);
        resetter?.Invoke(obj);
    }

    /// <summary>
    /// Возвращает арендованный объект в пул.
    /// </summary>
    /// <param name="obj">Арендованный объект.</param>
    public virtual void Return(T obj)
    {
        var currentIndex = Interlocked.Increment(ref index);

        if (currentIndex < capacity)
            pool[currentIndex] = obj;
        else
            Interlocked.Decrement(ref index);
    }

    /// <summary>
    /// Создает новый пул объектов.
    /// </summary>
    /// <param name="factory">Функция возврата значения из пула, если он пуст.</param>
    /// <param name="capacity">Размер пула.</param>
    public static ObjectPool<T> Create(Func<T> factory, int capacity) => new(factory, capacity);

    /// <summary>
    /// Создает новый пул объектов.
    /// </summary>
    /// <param name="factory">Функция возврата значения из пула, если он пуст.</param>
    public static ObjectPool<T> Create(Func<T> factory) => Create(factory, DefaultPoolSize);

    /// <summary>
    /// Создает новый пул объектов.
    /// </summary>
    /// <param name="capacity">Размер пула.</param>
    public static ObjectPool<T> Create(int capacity) => new(capacity);

    /// <summary>
    /// Создает новый пул объектов.
    /// </summary>
    public static ObjectPool<T> Create() => Create(DefaultPoolSize);

    /// <summary>
    /// Освобождает ресурсы, используемые объектом.
    /// </summary>
    /// <param name="disposing">Значение, указывающее, освобождать ли управляемые ресурсы.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (isDisposed) return;
        isDisposed = true;

        if (disposing) ArrayPool<T>.Shared.Return(pool);
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