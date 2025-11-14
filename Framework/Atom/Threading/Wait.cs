#pragma warning disable CA1052

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Atom.Threading;

/// <summary>
/// Представляет механизмы ожиданий.
/// </summary>
public class Wait
{
    private const int MaxSpinIterations = 8;
    private const int MaxYieldIterations = 8;
    private const int MaxSleepThreshold = 16;

    private static readonly int SpinWaitCount = Environment.ProcessorCount * 4;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Wait"/>.
    /// </summary>
    protected Wait() { }

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/>.
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Until([NotNull] Func<bool> condition, int timeout)
    {
        var spinner = new SpinWait();
        var timer = Stopwatch.StartNew();
        var counter = 0;
        var useHardwareSpin = true;

        while (!condition())
        {
            if (timeout != Timeout.Infinite && timer.ElapsedMilliseconds >= timeout) return;

            if (useHardwareSpin)
            {
                spinner.SpinOnce(sleep1Threshold: SpinWaitCount);

                if (spinner.Count % SpinWaitCount is 0)
                {
                    useHardwareSpin = default;
                    counter = MaxSpinIterations;
                }
            }
            else
            {
                var sleepTime = Math.Min(1 << (counter - MaxSpinIterations), MaxSleepThreshold);
                Thread.Sleep(sleepTime);
                counter = counter < MaxSpinIterations + MaxYieldIterations ? counter + 1 : MaxSpinIterations;
            }
        }
    }

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/>.
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Until(Func<bool> condition, TimeSpan timeout) => Until(condition, (int)timeout.TotalMilliseconds);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/>.
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Until(Func<bool> condition) => Until(condition, Timeout.Infinite);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask UntilAsync([NotNull] Func<bool> condition, int timeout, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var delay = 1;

        while (!condition())
        {
            if (timeout != Timeout.Infinite && timer.ElapsedMilliseconds >= timeout) return;

            if (delay <= MaxSleepThreshold)
            {
                await Task.Yield();
                delay <<= 1;
            }
            else
            {
                await Task.Delay(MaxSleepThreshold, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask UntilAsync(Func<bool> condition, int timeout) => UntilAsync(condition, timeout, CancellationToken.None);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask UntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken cancellationToken)
        => UntilAsync(condition, (int)timeout.TotalMilliseconds, cancellationToken);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask UntilAsync(Func<bool> condition, TimeSpan timeout)
        => UntilAsync(condition, timeout, CancellationToken.None);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask UntilAsync(Func<bool> condition, CancellationToken cancellationToken) => UntilAsync(condition, Timeout.Infinite, cancellationToken);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask UntilAsync(Func<bool> condition) => UntilAsync(condition, CancellationToken.None);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask UntilAsync([NotNull] Func<ValueTask<bool>> condition, int timeout, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var delay = 1;

        while (!await condition().ConfigureAwait(false))
        {
            if (timeout != Timeout.Infinite && timer.ElapsedMilliseconds >= timeout) return;

            if (delay <= MaxSleepThreshold)
            {
                await Task.Yield();
                delay <<= 1;
            }
            else
            {
                await Task.Delay(MaxSleepThreshold, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask UntilAsync(Func<ValueTask<bool>> condition, int timeout) => UntilAsync(condition, timeout, CancellationToken.None);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask UntilAsync(Func<ValueTask<bool>> condition, TimeSpan timeout, CancellationToken cancellationToken)
        => UntilAsync(condition, (int)timeout.TotalMilliseconds, cancellationToken);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask UntilAsync(Func<ValueTask<bool>> condition, TimeSpan timeout)
        => UntilAsync(condition, timeout, CancellationToken.None);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask UntilAsync(Func<ValueTask<bool>> condition, CancellationToken cancellationToken) => UntilAsync(condition, Timeout.Infinite, cancellationToken);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask UntilAsync(Func<ValueTask<bool>> condition) => UntilAsync(condition, CancellationToken.None);
}

/// <summary>
/// Представляет условное ожидание через сигналы.
/// </summary>
/// <typeparam name="T">Тип состояния.</typeparam>
public sealed class Wait<T> : Wait, IDisposable
{
    private readonly Locker locker = new(1);
    private readonly Signal<T> signal;
    private readonly T? state;
    private bool isDisposed;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Wait"/>
    /// </summary>
    /// <param name="signal">Связанный сигнал.</param>
    /// <param name="state">Связанное состояние.</param>
    public Wait(Signal<T> signal, T? state)
    {
        this.signal = signal;
        this.state = state;
        this.signal.Sended += OnSignaled;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Wait"/>
    /// </summary>
    /// <param name="signal">Связанный сигнал.</param>
    public Wait(Signal<T> signal) : this(signal, default) { }

    private void OnSignaled(object? sender, SignalEventArgs<T> args)
    {
        if (ReferenceEquals(args.State, state) || state?.Equals(args.State) is true || state is null || args.State is null) locker.Release();
    }

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask LockUntilAsync([NotNull] Func<bool> condition, int timeout, CancellationToken cancellationToken)
    {
        while (!Volatile.Read(ref isDisposed) && !condition())
        {
            await locker.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            await Task.Yield();
        }
    }

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockUntilAsync(Func<bool> condition, int timeout) => LockUntilAsync(condition, timeout, CancellationToken.None);

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockUntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken cancellationToken)
        => LockUntilAsync(condition, (int)timeout.TotalMilliseconds, cancellationToken);

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockUntilAsync(Func<bool> condition, TimeSpan timeout)
        => LockUntilAsync(condition, timeout, CancellationToken.None);

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
        => LockUntilAsync(condition, Timeout.Infinite, cancellationToken);

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockUntilAsync(Func<bool> condition) => LockUntilAsync(condition, CancellationToken.None);

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask LockUntilAsync([NotNull] Func<ValueTask<bool>> condition, int timeout, CancellationToken cancellationToken)
    {
        while (!Volatile.Read(ref isDisposed) && !await condition().ConfigureAwait(false))
        {
            await locker.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            await Task.Yield();
        }
    }

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockUntilAsync(Func<ValueTask<bool>> condition, int timeout) => LockUntilAsync(condition, timeout, CancellationToken.None);

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockUntilAsync(Func<ValueTask<bool>> condition, TimeSpan timeout, CancellationToken cancellationToken)
        => LockUntilAsync(condition, (int)timeout.TotalMilliseconds, cancellationToken);

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockUntilAsync(Func<ValueTask<bool>> condition, TimeSpan timeout)
        => LockUntilAsync(condition, timeout, CancellationToken.None);

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockUntilAsync(Func<ValueTask<bool>> condition, CancellationToken cancellationToken)
        => LockUntilAsync(condition, Timeout.Infinite, cancellationToken);

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockUntilAsync(Func<ValueTask<bool>> condition) => LockUntilAsync(condition, CancellationToken.None);

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref isDisposed, value: true, default)) return;

        signal.Sended -= OnSignaled;

        locker.Release();
        locker.Dispose();
    }
}