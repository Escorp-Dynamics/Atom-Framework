using System.Runtime.CompilerServices;

namespace Atom.Threading;

/// <summary>
/// Представляет механизм блокировки потоков.
/// </summary>
public sealed class Locker : IDisposable
{
    private readonly SemaphoreSlim locker;

    /// <summary>
    /// Количество оставшихся потоков, которые могут войти в ожидание.
    /// </summary>
    public int CurrentCount => locker.CurrentCount;

    /// <summary>
    /// <see cref="WaitHandle"/>, который используется для ожидания.
    /// </summary>
    public WaitHandle AvailableWaitHandle => locker.AvailableWaitHandle;

    /// <summary>
    /// Происходит в момент захода в ожидание.
    /// </summary>
    public event MutableEventHandler<object, MutableEventArgs>? Waiting;

    /// <summary>
    /// Происходит в момент выхода из ожидания.
    /// </summary>
    public event MutableEventHandler<object, LockerReleasedEventArgs>? Released;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Locker"/>.
    /// </summary>
    /// <param name="initialCount">Начальное число разблокированных потоков.</param>
    /// <param name="maxCount">Максимальное число разблокированных потоков.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Locker(int initialCount, int maxCount) => locker = new(initialCount, maxCount);

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Locker"/>.
    /// </summary>
    /// <param name="initialCount">Начальное число разблокированных потоков.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Locker(int initialCount) => locker = new(initialCount);

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Locker"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Locker() : this(1, 1) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OnWaiting() => Waiting?.On(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void OnReleased(int releaseCount) => Released?.On(this, args => args.ReleaseCount = releaseCount);

    /// <summary>
    /// Отправляет поток в ожидание.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Wait()
    {
        OnWaiting();
        locker.Wait();
    }

    /// <summary>
    /// Отправляет поток в ожидание.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Wait(CancellationToken cancellationToken)
    {
        OnWaiting();
        locker.Wait(cancellationToken);
    }

    /// <summary>
    /// Отправляет поток в ожидание.
    /// </summary>
    /// <param name="millisecondsTimeout">Таймаут ожидания (в миллисекундах).</param>
    /// <returns><c>True</c>, если текущий поток успешно зашёл в ожидание, иначе <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Wait(int millisecondsTimeout)
    {
        OnWaiting();
        return locker.Wait(millisecondsTimeout);
    }

    /// <summary>
    /// Отправляет поток в ожидание.
    /// </summary>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <returns><c>True</c>, если текущий поток успешно зашёл в ожидание, иначе <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Wait(TimeSpan timeout)
    {
        OnWaiting();
        return locker.Wait(timeout);
    }

    /// <summary>
    /// Отправляет поток в ожидание.
    /// </summary>
    /// <param name="millisecondsTimeout">Таймаут ожидания (в миллисекундах).</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns><c>True</c>, если текущий поток успешно зашёл в ожидание, иначе <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Wait(int millisecondsTimeout, CancellationToken cancellationToken)
    {
        OnWaiting();
        return locker.Wait(millisecondsTimeout, cancellationToken);
    }

    /// <summary>
    /// Отправляет поток в ожидание.
    /// </summary>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns><c>True</c>, если текущий поток успешно зашёл в ожидание, иначе <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Wait(TimeSpan timeout, CancellationToken cancellationToken)
    {
        OnWaiting();
        return locker.Wait(timeout, cancellationToken);
    }

    /// <summary>
    /// Отправляет поток в ожидание.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task WaitAsync()
    {
        OnWaiting();
        return locker.WaitAsync();
    }

    /// <summary>
    /// Отправляет поток в ожидание.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        OnWaiting();
        return locker.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Отправляет поток в ожидание.
    /// </summary>
    /// <param name="millisecondsTimeout">Таймаут ожидания (в миллисекундах).</param>
    /// <returns><c>True</c>, если текущий поток успешно зашёл в ожидание, иначе <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<bool> WaitAsync(int millisecondsTimeout)
    {
        OnWaiting();
        return locker.WaitAsync(millisecondsTimeout);
    }

    /// <summary>
    /// Отправляет поток в ожидание.
    /// </summary>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <returns><c>True</c>, если текущий поток успешно зашёл в ожидание, иначе <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<bool> WaitAsync(TimeSpan timeout)
    {
        OnWaiting();
        return locker.WaitAsync(timeout);
    }

    /// <summary>
    /// Отправляет поток в ожидание.
    /// </summary>
    /// <param name="millisecondsTimeout">Таймаут ожидания (в миллисекундах).</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns><c>True</c>, если текущий поток успешно зашёл в ожидание, иначе <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<bool> WaitAsync(int millisecondsTimeout, CancellationToken cancellationToken)
    {
        OnWaiting();
        return locker.WaitAsync(millisecondsTimeout, cancellationToken);
    }

    /// <summary>
    /// Отправляет поток в ожидание.
    /// </summary>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns><c>True</c>, если текущий поток успешно зашёл в ожидание, иначе <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        OnWaiting();
        return locker.WaitAsync(timeout, cancellationToken);
    }

    /// <summary>
    /// Выводит из ожидания заданное количество потоков.
    /// </summary>
    /// <param name="releaseCount">Количество потоков, которое требуется вывести из ожидания.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Release(int releaseCount)
    {
        locker.Release(releaseCount);
        OnReleased(releaseCount);
    }

    /// <summary>
    /// Выводит из ожидания один поток.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Release() => Release(1);

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() => locker.Dispose();

    /// <summary>
    /// Преобразует текущий экземпляр в строковое представление.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string? ToString() => locker.ToString();
}