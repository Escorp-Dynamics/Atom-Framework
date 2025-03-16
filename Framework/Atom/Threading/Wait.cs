using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Atom.Threading;

/// <summary>
/// Представляет механизмы ожиданий.
/// </summary>
public sealed class Wait : IDisposable
{
    private const int MaxSpinIterations = 8;
    private const int MaxYieldIterations = 8;
    private const int MaxSleepThreshold = 16;

    private readonly ManualResetEventSlim mre = new();
    private bool isDisposed;

    private static readonly int SpinWaitCount = Environment.ProcessorCount * 4;

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask LockAsync([NotNull] Func<bool> condition, int timeout, CancellationToken cancellationToken)
    {
        while (!Volatile.Read(ref isDisposed) && condition())
        {
            mre.Wait(timeout, cancellationToken);
            mre.Reset();
            await Task.Yield();
        }
    }

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockAsync(Func<bool> condition, int timeout) => LockAsync(condition, timeout, CancellationToken.None);

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockAsync(Func<bool> condition, TimeSpan timeout, CancellationToken cancellationToken)
        => LockAsync(condition, (int)timeout.TotalMilliseconds, cancellationToken);

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockAsync(Func<bool> condition, TimeSpan timeout)
        => LockAsync(condition, timeout, CancellationToken.None);

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockAsync(Func<bool> condition, CancellationToken cancellationToken)
        => LockAsync(condition, Timeout.Infinite, cancellationToken);

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockAsync(Func<bool> condition) => LockAsync(condition, CancellationToken.None);

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask LockAsync([NotNull] Func<ValueTask<bool>> condition, int timeout, CancellationToken cancellationToken)
    {
        while (!Volatile.Read(ref isDisposed) && await condition().ConfigureAwait(false))
        {
            mre.Wait(timeout, cancellationToken);
            mre.Reset();
            await Task.Yield();
        }
    }

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockAsync(Func<ValueTask<bool>> condition, int timeout) => LockAsync(condition, timeout, CancellationToken.None);

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockAsync(Func<ValueTask<bool>> condition, TimeSpan timeout, CancellationToken cancellationToken)
        => LockAsync(condition, (int)timeout.TotalMilliseconds, cancellationToken);

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockAsync(Func<ValueTask<bool>> condition, TimeSpan timeout)
        => LockAsync(condition, timeout, CancellationToken.None);

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockAsync(Func<ValueTask<bool>> condition, CancellationToken cancellationToken)
        => LockAsync(condition, Timeout.Infinite, cancellationToken);

    /// <summary>
    /// Ожидает выполнения условия.
    /// </summary>
    /// <param name="condition">Условие.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask LockAsync(Func<ValueTask<bool>> condition) => LockAsync(condition, CancellationToken.None);

    /// <summary>
    /// Освобождает ожидание.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Release() => mre.Set();

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;

        mre.Set();
        mre.Dispose();

        GC.SuppressFinalize(this);
    }

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

        while (condition())
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

        while (condition())
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

        while (await condition().ConfigureAwait(false))
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