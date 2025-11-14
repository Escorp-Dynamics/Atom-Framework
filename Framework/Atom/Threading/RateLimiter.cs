using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Atom.Threading;

/// <summary>
/// Механизм ограничения частоты выполнения операций с поддержкой динамической настройки параметров.
/// </summary>
public sealed class RateLimiter : IDisposable
{
    private readonly ResettableCancellationTokenSource cts = new();

    private long[] timestamps;
    private volatile int limit;
    private long rateTicks;
    private bool isDisposed;

    private static readonly long ticksPerSecond = Stopwatch.Frequency;
    private static readonly long ticksPerMillisecond = ticksPerSecond / 1000;

    /// <summary>
    /// Максимальное количество операций, разрешённых за интервал <see cref="Rate"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Возникает при попытке установить значение меньше или равное 0.
    /// </exception>
    /// <remarks>
    /// При увеличении значения производится потокобезопасное расширение внутреннего буфера.
    /// </remarks>
    public int Limit
    {
        get => limit;

        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            if (value == limit) return;

            var newArray = ArrayPool<long>.Shared.Rent(value);
            var now = Stopwatch.GetTimestamp();
            Array.Fill(newArray, now - rateTicks);

            var oldArray = Interlocked.Exchange(ref timestamps, newArray);
            ArrayPool<long>.Shared.Return(oldArray);
            limit = value;
        }
    }

    /// <summary>
    /// Временной интервал, в течение которого разрешено не более <see cref="Limit"/> операций.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Возникает при попытке установить неположительное значение.
    /// </exception>
    /// <remarks>
    /// Хранится с точностью до тика (100 наносекунд).
    /// </remarks>
    public TimeSpan Rate
    {
        get => TimeSpan.FromMilliseconds((double)(rateTicks * ticksPerMillisecond) / ticksPerSecond);

        set
        {
            var ms = value.TotalMilliseconds;
            if (ms <= 0) throw new ArgumentException("Rate должен быть положительным", nameof(value));
            rateTicks = (long)(value.TotalSeconds * ticksPerSecond);
        }
    }

    /// <summary>
    /// Инициализирует новый экземпляр ограничителя частоты.
    /// </summary>
    /// <param name="limit">Максимальное количество операций.</param>
    /// <param name="rate">Интервал времени для заданного лимита операций.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Возникает при некорректных значениях параметров.
    /// </exception>
    public RateLimiter(int limit, TimeSpan rate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        if (rate <= TimeSpan.Zero) throw new ArgumentException("Rate должен быть положительным", nameof(rate));

        this.limit = limit;
        Rate = rate;
        timestamps = ArrayPool<long>.Shared.Rent(limit);
        Array.Fill(timestamps, Stopwatch.GetTimestamp() - rateTicks);
    }

    /// <summary>
    /// Инициализирует новый экземпляр ограничителя частоты с указанием интервала в миллисекундах.
    /// </summary>
    /// <param name="limit">Максимальное количество операций.</param>
    /// <param name="rateMilliseconds">Интервал времени в миллисекундах.</param>
    public RateLimiter(int limit, int rateMilliseconds) : this(limit, TimeSpan.FromMilliseconds(rateMilliseconds)) { }

    private async ValueTask<bool> WaitAsync(CancellationToken cancellationToken)
    {
        var spinWait = new SpinWait();

        while (!Volatile.Read(ref isDisposed) && !cancellationToken.IsCancellationRequested)
        {
            var now = Stopwatch.GetTimestamp();
            var cutoff = now - rateTicks;

            if (TryAcquireSlot(timestamps, limit, now, cutoff)) return true;

            var waitTicks = CalculateRequiredWait(timestamps, limit, now, rateTicks);

            if (waitTicks <= 0)
            {
                spinWait.SpinOnce();
                continue;
            }

            var delay = TimeSpan.FromTicks(waitTicks * 1000_000 / ticksPerSecond);

            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return default;
            }
        }

        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask CallInternalAsync(Action callback)
    {
        if (await WaitAsync(cts.Token).ConfigureAwait(false)) callback();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask CallInternalAsync(Func<ValueTask> callback)
    {
        if (await WaitAsync(cts.Token).ConfigureAwait(false)) await callback().ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask<TResult> CallInternalAsync<TResult>(Func<TResult> callback)
    {
        if (await WaitAsync(cts.Token).ConfigureAwait(false)) return callback();
        return default!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask<TResult> CallInternalAsync<TResult>(Func<ValueTask<TResult>> callback)
    {
        if (await WaitAsync(cts.Token).ConfigureAwait(false)) return await callback().ConfigureAwait(false);
        return default!;
    }

    /// <summary>
    /// Выполняет синхронную операцию с учётом ограничений частоты.
    /// </summary>
    /// <param name="callback">Синхронный делегат для выполнения.</param>
    /// <returns>Задача, завершающаяся после выполнения операции.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask CallAsync([NotNull] Action callback) => CallInternalAsync(callback);

    /// <summary>
    /// Выполняет асинхронную операцию с учётом ограничений частоты.
    /// </summary>
    /// <param name="callback">Асинхронный делегат без возвращаемого значения.</param>
    /// <returns>Задача, завершающаяся после выполнения операции.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask CallAsync([NotNull] Func<ValueTask> callback) => CallInternalAsync(callback);

    /// <summary>
    /// Выполняет синхронную операцию с возвратом результата.
    /// </summary>
    /// <typeparam name="TResult">Тип возвращаемого значения.</typeparam>
    /// <param name="callback">Синхронный делегат с возвратом значения.</param>
    /// <returns>Задача с результатом выполнения операции.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<TResult> CallAsync<TResult>([NotNull] Func<TResult> callback) => CallInternalAsync(callback);

    /// <summary>
    /// Выполняет асинхронную операцию с возвратом результата.
    /// </summary>
    /// <typeparam name="TResult">Тип возвращаемого значения.</typeparam>
    /// <param name="callback">Асинхронный делегат с возвратом значения.</param>
    /// <returns>Задача с результатом выполнения операции.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<TResult> CallAsync<TResult>([NotNull] Func<ValueTask<TResult>> callback) => CallInternalAsync(callback);

    /// <summary>
    /// Сбрасывает все ожидания.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset() => cts.Reset();

    /// <summary>
    /// Освобождает ресурсы ограничителя частоты.
    /// </summary>
    /// <remarks>
    /// После вызова Dispose все последующие вызовы будут вызывать исключение.
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref isDisposed, value: true, default)) return;

        cts.Dispose();
        ArrayPool<long>.Shared.Return(timestamps);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryAcquireSlot(Span<long> timestamps, int limit, long now, long cutoff)
    {
        for (var i = 0; i < limit; ++i)
        {
            var idx = i % limit;
            var oldTimestamp = Volatile.Read(ref timestamps[idx]);

            if (oldTimestamp > cutoff) continue;

            if (Interlocked.CompareExchange(ref timestamps[idx], now, oldTimestamp) == oldTimestamp) return true;
        }

        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long CalculateRequiredWait(Span<long> timestamps, int limit, long now, long rateTicks)
    {
        var minTimestamp = long.MaxValue;
        var currentCutoff = now - rateTicks;

        for (var i = 0; i < limit; ++i)
        {
            var ts = Volatile.Read(ref timestamps[i]);
            if (ts > currentCutoff && ts < minTimestamp) minTimestamp = ts;
        }

        if (minTimestamp is long.MaxValue) return default;

        var requiredWait = minTimestamp + rateTicks - now;
        return requiredWait > 0 ? requiredWait : default;
    }
}