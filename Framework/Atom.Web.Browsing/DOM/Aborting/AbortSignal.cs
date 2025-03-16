using System.Diagnostics.CodeAnalysis;
using Microsoft.ClearScript;

namespace Atom.Web.Browsing.DOM;

/// <summary>
/// Представляет собой объект сигнала, который позволяет вам общаться с DOM запросом (например, Fetch) и прервать его при необходимости с помощью объекта AbortController.
/// </summary>
public class AbortSignal : EventTarget, IAbortSignal
{
    /// <inheritdoc/>
    [ScriptMember("aborted", ScriptAccess.ReadOnly)]
    public bool IsAborted { get; set; }

    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public Exception Reason { get; set; }

    /// <inheritdoc/>
    [ScriptMember("onabort")]
    public event Action<IEvent>? Aborted;

    internal AbortSignal(bool isAborted, Exception reason) => (IsAborted, Reason) = (isAborted, reason);

    internal AbortSignal(bool isAborted) : this(isAborted, new OperationCanceledException("Операция отменена")) { }

    internal AbortSignal(Exception reason) : this(default, reason) { }

    internal AbortSignal() : this(false) { }

    /// <inheritdoc/>
    [ScriptMember]
    public void ThrowIfAborted()
    {
        if (IsAborted) throw Reason;
    }

    /// <inheritdoc/>
    [ScriptMember]
    public static AbortSignal Abort(Exception reason) => new(true, reason);

    /// <inheritdoc/>
    [ScriptMember]
    public static AbortSignal Abort() => new(true);

    /// <inheritdoc/>
    [ScriptMember]
    public static AbortSignal Timeout(ulong milliseconds)
    {
        var signal = new AbortSignal();

        Task.Delay((int)milliseconds).ContinueWith(_ =>
        {
            signal.IsAborted = true;
            signal.Reason = new TimeoutException($"Signal timed out after {milliseconds} milliseconds");
            signal.DispatchEvent(new Event("abort"));
        }, TaskScheduler.Current);

        return signal;
    }

    /// <inheritdoc/>
    [ScriptMember]
    public static AbortSignal Any([NotNull] IEnumerable<AbortSignal> signals)
    {
        var combinedSignal = new AbortSignal();

        foreach (var signal in signals)
        {
            signal.Aborted = e =>
            {
                combinedSignal.IsAborted = true;
                combinedSignal.Reason = signal.Reason;
                combinedSignal.DispatchEvent(new Event("abort"));
            };
        }

        return combinedSignal;
    }
}