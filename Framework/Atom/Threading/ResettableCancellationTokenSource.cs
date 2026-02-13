using System.Runtime.CompilerServices;

#pragma warning disable CA2000, IDISP003, IDISP007, IDISP017, IDE0022

namespace Atom.Threading;

/// <summary>
/// Представляет источник токена отмены, который можно сбрасывать в исходное состояние.
/// </summary>
public sealed class ResettableCancellationTokenSource : CancellationTokenSource
{
    private CtsHolder holder;
    private bool isDisposed;

    /// <summary>
    /// Получает текущий токен отмены.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Источник токена был освобождён.</exception>
    public new CancellationToken Token
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);
            return WithCts(cts => cts.Token);
        }
    }

    /// <summary>
    /// Определяет, был ли токен отменён.
    /// </summary>
    public new bool IsCancellationRequested
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => WithCts(cts => cts.IsCancellationRequested);
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ResettableCancellationTokenSource"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ResettableCancellationTokenSource() => holder = new CtsHolder(new CancellationTokenSource());

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ResettableCancellationTokenSource"/>.
    /// </summary>
    /// <param name="delay">Задержка перед отменой.</param>
    /// <param name="timeProvider">Провайдер таймера.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ResettableCancellationTokenSource(TimeSpan delay, TimeProvider timeProvider) => holder = new CtsHolder(new CancellationTokenSource(delay, timeProvider));

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ResettableCancellationTokenSource"/>.
    /// </summary>
    /// <param name="delay">Задержка перед отменой.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ResettableCancellationTokenSource(TimeSpan delay) => holder = new CtsHolder(new CancellationTokenSource(delay));

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ResettableCancellationTokenSource"/>.
    /// </summary>
    /// <param name="millisecondsDelay">Задержка перед отменой.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ResettableCancellationTokenSource(int millisecondsDelay) => holder = new CtsHolder(new CancellationTokenSource(millisecondsDelay));

    /// <summary>
    /// Выполняет операцию на текущем CTS с защитой от dispose.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private T WithCts<T>(Func<CancellationTokenSource, T> action, T fallback = default!)
    {
        var h = Volatile.Read(ref holder);
        h.AddRef();
        try { return action(h.Cts); }
        catch (ObjectDisposedException) { return fallback; }
        finally { h.Release(); }
    }

    /// <summary>
    /// Выполняет операцию на текущем CTS с защитой от dispose.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WithCts(Action<CancellationTokenSource> action)
    {
        var h = Volatile.Read(ref holder);
        h.AddRef();
        try { action(h.Cts); }
        catch (ObjectDisposedException) { /* CTS был disposed во время операции — игнорируем */ }
        finally { h.Release(); }
    }

    /// <summary>
    /// Выполняет асинхронную операцию на текущем CTS с защитой от dispose.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task WithCtsAsync(Func<CancellationTokenSource, Task> action)
    {
        var h = Volatile.Read(ref holder);
        h.AddRef();
        try { await action(h.Cts).ConfigureAwait(false); }
        catch (ObjectDisposedException) { /* CTS был disposed во время операции — игнорируем */ }
        finally { h.Release(); }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Reset(CancellationTokenSource newCts)
    {
        if (Volatile.Read(ref isDisposed))
        {
            newCts.Dispose();
            return;
        }

        var newHolder = new CtsHolder(newCts);
        var previousHolder = Interlocked.Exchange(ref holder, newHolder);

        try { previousHolder.MarkForDisposal(); }
        catch (ObjectDisposedException) { /* Уже disposed — игнорируем */ }
    }

    /// <summary>
    /// Отменяет текущий токен.
    /// </summary>
    /// <param name="throwOnFirstException">Указывает, следует ли выбрасывать первое исключение.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new void Cancel(bool throwOnFirstException) => WithCts(cts => cts.Cancel(throwOnFirstException));

    /// <summary>
    /// Отменяет текущий токен.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new void Cancel() => WithCts(cts => cts.Cancel());

    /// <summary>
    /// Асинхронно отменяет текущий токен.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new ValueTask CancelAsync() => new(WithCtsAsync(cts => cts.CancelAsync()));

    /// <summary>
    /// Отменяет текущий токен после задержки.
    /// </summary>
    /// <param name="delay">Задержка перед отменой.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new void CancelAfter(TimeSpan delay) => WithCts(cts => cts.CancelAfter(delay));

    /// <summary>
    /// Отменяет текущий токен после задержки.
    /// </summary>
    /// <param name="millisecondsDelay">Задержка перед отменой.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new void CancelAfter(int millisecondsDelay) => WithCts(cts => cts.CancelAfter(millisecondsDelay));

    /// <summary>
    /// Пытается сбросить состояние токена отмены, если он ещё не был отменён.
    /// </summary>
    /// <returns><see langword="true"/> если сброс удался; иначе <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new bool TryReset() => WithCts(cts => cts.TryReset());

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
        if (disposing) holder.MarkForDisposal();
    }

    /// <summary>
    /// Обёртка над CTS с подсчётом ссылок для безопасного Dispose.
    /// </summary>
    /// <param name="cts">Внутренний CancellationTokenSource.</param>
    private sealed class CtsHolder(CancellationTokenSource cts)
    {
        public readonly CancellationTokenSource Cts = cts;
        private int refCount = 1; // Начинаем с 1 — владелец
        private bool markedForDisposal;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRef() => Interlocked.Increment(ref refCount);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Release()
        {
            if (Interlocked.Decrement(ref refCount) is 0 && Volatile.Read(ref markedForDisposal))
                Cts.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MarkForDisposal()
        {
            Volatile.Write(ref markedForDisposal, value: true);
            Release(); // Уменьшаем счётчик владельца
        }
    }
}

#pragma warning restore CA2000, IDISP003, IDISP007, IDISP017, IDE0022
