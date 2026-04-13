using System.Runtime.CompilerServices;

namespace Atom.Net.Browsing.WebDriver;

internal enum CallbackControlAction
{
    Continue,
    Abort,
    Replace,
}

internal sealed class CallbackDecision
{
    public required CallbackControlAction Action { get; init; }

    public object?[]? Args { get; init; }

    public string? Code { get; init; }

    public static CallbackDecision Continue(object?[]? args = null)
        => new()
        {
            Action = CallbackControlAction.Continue,
            Args = args,
        };

    public static CallbackDecision Abort()
        => new()
        {
            Action = CallbackControlAction.Abort,
        };

    public static CallbackDecision Replace(string code)
        => new()
        {
            Action = CallbackControlAction.Replace,
            Code = code,
        };
}

/// <summary>
/// Содержит данные callback-вызова из браузерного окружения.
/// </summary>
public sealed class CallbackEventArgs : MutableEventArgs
{
    private readonly TaskCompletionSource<CallbackDecision> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Получает имя callback-обработчика.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Получает аргументы callback-вызова.
    /// </summary>
    public object?[] Args { get; init; } = [];

    /// <summary>
    /// Получает текущее тело callback-вызова в виде JavaScript-кода, если оно доступно.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// Прерывает выполнение callback-вызова.
    /// </summary>
    public ValueTask AbortAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IsCancelled = true;
        decision.TrySetResult(CallbackDecision.Abort());

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Прерывает выполнение callback-вызова.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask AbortAsync()
        => AbortAsync(CancellationToken.None);

    /// <summary>
    /// Подменяет тело callback-вызова новым кодом и отдает выполнение.
    /// </summary>
    public ValueTask ReplaceAsync(string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        cancellationToken.ThrowIfCancellationRequested();

        decision.TrySetResult(CallbackDecision.Replace(code));

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Подменяет тело callback-вызова новым кодом и отдает выполнение.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask ReplaceAsync(string code)
        => ReplaceAsync(code, CancellationToken.None);

    /// <summary>
    /// Продолжает выполнение callback-вызова без изменений.
    /// </summary>
    public ValueTask ContinueAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        decision.TrySetResult(CallbackDecision.Continue());

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Продолжает выполнение callback-вызова без изменений.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask ContinueAsync()
        => ContinueAsync(CancellationToken.None);

    /// <summary>
    /// Продолжает выполнение callback-вызова с новыми аргументами.
    /// </summary>
    public ValueTask ContinueAsync(object?[] args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        cancellationToken.ThrowIfCancellationRequested();

        decision.TrySetResult(CallbackDecision.Continue([.. args]));

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Продолжает выполнение callback-вызова с новыми аргументами.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask ContinueAsync(object?[] args)
        => ContinueAsync(args, CancellationToken.None);

    internal Task<CallbackDecision> WaitForDecisionAsync(CancellationToken cancellationToken)
        => decision.Task.WaitAsync(cancellationToken);

    internal void SetDefaultIfPending()
        => decision.TrySetResult(CallbackDecision.Continue());
}