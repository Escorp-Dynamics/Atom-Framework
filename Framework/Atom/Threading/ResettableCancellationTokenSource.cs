using System.Runtime.CompilerServices;

#pragma warning disable IDISP003, IDISP007, IDISP017, IDE0022

namespace Atom.Threading;

/// <summary>
/// Представляет источник токена отмены, который можно сбрасывать в исходное состояние.
/// </summary>
public sealed class ResettableCancellationTokenSource : CancellationTokenSource
{
    private CancellationTokenSource cts;
    private bool isDisposed;

    /// <summary>
    /// Получает текущий токен отмены.
    /// </summary>
    public new CancellationToken Token => cts.Token;

    /// <summary>
    /// Определяет, был ли токен отменён.
    /// </summary>
    public new bool IsCancellationRequested
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => cts.IsCancellationRequested;
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ResettableCancellationTokenSource"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ResettableCancellationTokenSource() => cts = new CancellationTokenSource();

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ResettableCancellationTokenSource"/>.
    /// </summary>
    /// <param name="delay">Задержка перед отменой.</param>
    /// <param name="timeProvider">Провайдер таймера.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ResettableCancellationTokenSource(TimeSpan delay, TimeProvider timeProvider) => cts = new CancellationTokenSource(delay, timeProvider);

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ResettableCancellationTokenSource"/>.
    /// </summary>
    /// <param name="delay">Задержка перед отменой.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ResettableCancellationTokenSource(TimeSpan delay) => cts = new CancellationTokenSource(delay);

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ResettableCancellationTokenSource"/>.
    /// </summary>
    /// <param name="millisecondsDelay">Задержка перед отменой.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ResettableCancellationTokenSource(int millisecondsDelay) => cts = new CancellationTokenSource(millisecondsDelay);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Reset(CancellationTokenSource newCts)
    {
        if (Volatile.Read(ref isDisposed))
        {
            newCts.Dispose();
            return;
        }

        var previousCts = Interlocked.Exchange(ref cts, newCts);
#pragma warning disable IDISP007 // Источник создаётся и освобождается текущим типом.
        previousCts.Dispose();
#pragma warning restore IDISP007
    }

    /// <summary>
    /// Отменяет текущий токен.
    /// </summary>
    /// <param name="throwOnFirstException">Указывает, следует ли выбрасывать первое исключение.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new void Cancel(bool throwOnFirstException) => Volatile.Read(ref cts).Cancel(throwOnFirstException);

    /// <summary>
    /// Отменяет текущий токен.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new void Cancel() => Volatile.Read(ref cts).Cancel();

    /// <summary>
    /// Асинхронно отменяет текущий токен.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new async ValueTask CancelAsync() => await Volatile.Read(ref cts).CancelAsync().ConfigureAwait(false);

    /// <summary>
    /// Отменяет текущий токен после задержки.
    /// </summary>
    /// <param name="delay">Задержка перед отменой.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new void CancelAfter(TimeSpan delay) => Volatile.Read(ref cts).CancelAfter(delay);

    /// <summary>
    /// Отменяет текущий токен после задержки.
    /// </summary>
    /// <param name="millisecondsDelay">Задержка перед отменой.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new void CancelAfter(int millisecondsDelay) => Volatile.Read(ref cts).CancelAfter(millisecondsDelay);

    /// <summary>
    /// Пытается сбросить состояние токена отмены, если он ещё не был отменён.
    /// </summary>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new bool TryReset() => Volatile.Read(ref cts).TryReset();

    /// <summary>
    /// Сбрасывает источник токена.
    /// </summary>
    /// <param name="delay">Задержка перед отменой.</param>
    /// <param name="timeProvider">Провайдер таймера.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset(TimeSpan delay, TimeProvider timeProvider)
    {
#pragma warning disable IDISP003, IDISP017
        Reset(new CancellationTokenSource(delay, timeProvider));
#pragma warning restore IDISP003, IDISP017
    }

    /// <summary>
    /// Сбрасывает источник токена.
    /// </summary>
    /// <param name="delay">Задержка перед отменой.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset(TimeSpan delay)
    {
#pragma warning disable IDISP003, IDISP017
        Reset(new CancellationTokenSource(delay));
#pragma warning restore IDISP003, IDISP017
    }

    /// <summary>
    /// Сбрасывает источник токена.
    /// </summary>
    /// <param name="millisecondsDelay">Задержка перед отменой.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset(int millisecondsDelay)
    {
#pragma warning disable IDISP003, IDISP017
        Reset(new CancellationTokenSource(millisecondsDelay));
#pragma warning restore IDISP003, IDISP017
    }

    /// <summary>
    /// Сбрасывает источник токена.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
#pragma warning disable IDISP003, IDISP017
        Reset(new CancellationTokenSource());
#pragma warning restore IDISP003, IDISP017
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref isDisposed, value: true, default)) return;
        base.Dispose(disposing);
        if (disposing) cts.Dispose();
    }
}

#pragma warning restore IDISP003, IDISP007, IDISP017, IDE0022
