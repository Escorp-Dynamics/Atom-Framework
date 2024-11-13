using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Atom.Threading;

/// <summary>
/// Представляет механизм ограничения числа потоков в единицу времени.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="RateLimiter"/>.
/// </remarks>
/// <param name="limit">Максимальное число потоков, работающих в единицу времени <see cref="Rate"/>.</param>
/// <param name="rate">Единица времени, за которое будет запущено <see cref="Limit"/> потоков.</param>
public sealed class RateLimiter(int limit, TimeSpan rate) : IDisposable
{
    private readonly Stopwatch timer = Stopwatch.StartNew();
    private readonly SemaphoreSlim locker = new(limit);

    /// <summary>
    /// Максимальное число потоков, работающих в единицу времени <see cref="Rate"/>.
    /// </summary>
    public int Limit
    {
        get;

        set
        {
            field = value;
            var difference = field - locker.CurrentCount;

            if (difference > 0)
            {
                locker.Release(difference);
            }
            else if (difference < 0)
            {
                for (var i = 0; i < -difference; ++i) locker.Wait();
            }
        }
    } = limit;

    /// <summary>
    /// Единица времени, за которое будет запущено <see cref="Limit"/> потоков.
    /// </summary>
    public TimeSpan Rate { get; set; } = rate;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="RateLimiter"/>.
    /// </summary>
    /// <param name="limit">Максимальное число потоков, работающих в единицу времени <see cref="Rate"/>.</param>
    /// <param name="rate">Единица времени, за которое будет запущено <see cref="Limit"/> потоков (миллисекунд).</param>
    public RateLimiter(int limit, int rate) : this(limit, TimeSpan.FromMilliseconds(rate)) { }

    /// <summary>
    /// Выполняет указанный делегат с ограничением по частоте вызовов.
    /// </summary>
    /// <param name="callback">Делегат, который необходимо выполнить.</param>
    /// <exception cref="OperationCanceledException">Выбрасывается, если операция была отменена.</exception>
    public void Call([NotNull] Action callback)
    {
        if (timer.Elapsed > Rate)
        {
            while (locker.CurrentCount < (locker.CurrentCount + locker.CurrentCount)) locker.Release();
        }

        locker.Wait();

        try
        {
            callback.Invoke();
        }
        finally
        {
            timer.Restart();
            locker.Release();
        }
    }

    /// <summary>
    /// Выполняет указанный делегат с ограничением по частоте вызовов и возвращает результат.
    /// </summary>
    /// <typeparam name="T">Тип возвращаемого значения делегата.</typeparam>
    /// <param name="callback">Делегат, который необходимо выполнить.</param>
    /// <returns>Возвращает результат выполнения делегата.</returns>
    /// <exception cref="OperationCanceledException">Выбрасывается, если операция была отменена.</exception>
    public T Call<T>([NotNull] Func<T> callback)
    {
        if (timer.Elapsed > Rate)
        {
            while (locker.CurrentCount < (locker.CurrentCount + locker.CurrentCount)) locker.Release();
        }

        locker.Wait();

        try
        {
            return callback();
        }
        finally
        {
            timer.Restart();
            locker.Release();
        }
    }

    /// <summary>
    /// Асинхронно выполняет указанный делегат с ограничением по частоте вызовов.
    /// </summary>
    /// <param name="callback">Делегат, который необходимо выполнить.</param>
    /// <param name="cancellationToken">Токен отмены для отмены ожидания.</param>
    /// <returns>Возвращает задачу, представляющую асинхронную операцию.</returns>
    /// <exception cref="OperationCanceledException">Выбрасывается, если операция была отменена.</exception>
    public async ValueTask CallAsync([NotNull] Func<ValueTask> callback, CancellationToken cancellationToken)
    {
        if (timer.Elapsed > Rate)
        {
            while (locker.CurrentCount < (locker.CurrentCount + locker.CurrentCount)) locker.Release();
        }

        await locker.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await callback().ConfigureAwait(false);
        }
        finally
        {
            timer.Restart();
            locker.Release();
        }
    }

    /// <summary>
    /// Асинхронно выполняет указанный делегат с ограничением по частоте вызовов.
    /// </summary>
    /// <param name="callback">Делегат, который необходимо выполнить.</param>
    /// <returns>Возвращает задачу, представляющую асинхронную операцию.</returns>
    /// <exception cref="OperationCanceledException">Выбрасывается, если операция была отменена.</exception>
    public ValueTask CallAsync(Func<ValueTask> callback) => CallAsync(callback, CancellationToken.None);

    /// <summary>
    /// Асинхронно выполняет указанный делегат с ограничением по частоте вызовов.
    /// </summary>
    /// <typeparam name="T">Тип возвращаемого значения делегата.</typeparam>
    /// <param name="callback">Делегат, который необходимо выполнить.</param>
    /// <param name="cancellationToken">Токен отмены для отмены ожидания.</param>
    /// <returns>Возвращает задачу, представляющую асинхронную операцию.</returns>
    /// <exception cref="OperationCanceledException">Выбрасывается, если операция была отменена.</exception>
    public async ValueTask<T> CallAsync<T>([NotNull] Func<ValueTask<T>> callback, CancellationToken cancellationToken)
    {
        if (timer.Elapsed > Rate)
        {
            while (locker.CurrentCount < (locker.CurrentCount + locker.CurrentCount)) locker.Release();
        }

        await locker.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await callback().ConfigureAwait(false);
            return result;
        }
        finally
        {
            timer.Restart();
            locker.Release();
        }
    }

    /// <summary>
    /// Асинхронно выполняет указанный делегат с ограничением по частоте вызовов.
    /// </summary>
    /// <typeparam name="T">Тип возвращаемого значения делегата.</typeparam>
    /// <param name="callback">Делегат, который необходимо выполнить.</param>
    /// <returns>Возвращает задачу, представляющую асинхронную операцию.</returns>
    /// <exception cref="OperationCanceledException">Выбрасывается, если операция была отменена.</exception>
    public ValueTask<T> CallAsync<T>(Func<ValueTask<T>> callback) => CallAsync(callback, CancellationToken.None);

    /// <summary>
    /// Освобождает неуправляемые ресурсы, используемые <see cref="RateLimiter"/>, и предотвращает его удаление сборщиком мусора.
    /// </summary>
    public void Dispose()
    {
        locker.Dispose();
        GC.SuppressFinalize(this);
    }
}