using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Atom.Threading;

/// <summary>
/// Представляет механизмы ожиданий.
/// </summary>
public static class Wait
{
    private static SpinWait spin;

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/>.
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="interval">Интервал между итерациями ожидания (в миллисекундах).</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    public static void Until(Func<bool> condition, int interval, int timeout)
    {
        ArgumentNullException.ThrowIfNull(condition, nameof(condition));
        if (interval <= 0) interval = 1;

        var timer = Stopwatch.StartNew();
        var isInfinite = timeout is Timeout.Infinite;

        while (condition() && (isInfinite || timer.ElapsedMilliseconds < timeout)) spin.SpinOnce(interval);
    }

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/>.
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="interval">Интервал между итерациями ожидания.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    public static void Until(Func<bool> condition, TimeSpan interval, TimeSpan timeout)
        => Until(condition, (int)interval.TotalMilliseconds, (int)timeout.TotalMilliseconds);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/>.
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="interval">Интервал между итерациями ожидания (в миллисекундах).</param>
    public static void Until(Func<bool> condition, int interval) => Until(condition, interval, Timeout.Infinite);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/>.
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="interval">Интервал между итерациями ожидания.</param>
    public static void Until(Func<bool> condition, TimeSpan interval) => Until(condition, (int)interval.TotalMilliseconds);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/>.
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    public static void Until(Func<bool> condition) => Until(condition, 1);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="interval">Интервал между итерациями ожидания (в миллисекундах).</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    public static async ValueTask UntilAsync([NotNull] Func<bool> condition, int interval, int timeout, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(interval, nameof(interval));
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, -1, nameof(timeout));

        var timer = Stopwatch.StartNew();
        var isInfinite = timeout is Timeout.Infinite;

        while (condition() && (isInfinite || timer.ElapsedMilliseconds < timeout)) await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="interval">Интервал между итерациями ожидания (в миллисекундах).</param>
    /// <param name="timeout">Таймаут ожидания (в миллисекундах).</param>
    /// <returns></returns>
    public static ValueTask UntilAsync(Func<bool> condition, int interval, int timeout) => UntilAsync(condition, interval, timeout, CancellationToken.None);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="interval">Интервал между итерациями ожидания.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    public static ValueTask UntilAsync(Func<bool> condition, TimeSpan interval, TimeSpan timeout, CancellationToken cancellationToken)
        => UntilAsync(condition, (int)interval.TotalMilliseconds, (int)timeout.TotalMilliseconds, cancellationToken);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="interval">Интервал между итерациями ожидания.</param>
    /// <param name="timeout">Таймаут ожидания.</param>
    /// <returns></returns>
    public static ValueTask UntilAsync(Func<bool> condition, TimeSpan interval, TimeSpan timeout)
        => UntilAsync(condition, interval, timeout, CancellationToken.None);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="interval">Интервал между итерациями ожидания (в миллисекундах).</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    public static ValueTask UntilAsync(Func<bool> condition, int interval, CancellationToken cancellationToken)
        => UntilAsync(condition, interval, Timeout.Infinite, cancellationToken);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="interval">Интервал между итерациями ожидания (в миллисекундах).</param>
    /// <returns></returns>
    public static ValueTask UntilAsync(Func<bool> condition, int interval) => UntilAsync(condition, interval, CancellationToken.None);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="interval">Интервал между итерациями ожидания.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    public static ValueTask UntilAsync(Func<bool> condition, TimeSpan interval, CancellationToken cancellationToken)
        => UntilAsync(condition, (int)interval.TotalMilliseconds, cancellationToken);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="interval">Интервал между итерациями ожидания.</param>
    /// <returns></returns>
    public static ValueTask UntilAsync(Func<bool> condition, TimeSpan interval) => UntilAsync(condition, interval, CancellationToken.None);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns></returns>
    public static ValueTask UntilAsync(Func<bool> condition, CancellationToken cancellationToken) => UntilAsync(condition, 1, cancellationToken);

    /// <summary>
    /// Ожидает до тех пор, пока выполняется условие <paramref name="condition"/> (не блокируя поток).
    /// </summary>
    /// <param name="condition">Условие ожидания.</param>
    /// <returns></returns>
    public static ValueTask UntilAsync(Func<bool> condition) => UntilAsync(condition, CancellationToken.None);
}