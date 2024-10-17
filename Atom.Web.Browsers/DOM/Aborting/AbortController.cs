using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет объект контроллера, который позволяет вам при необходимости обрывать один и более DOM запросов.
/// </summary>
public class AbortController : IAbortController
{
    /// <inheritdoc/>
    [ScriptMember(ScriptAccess.ReadOnly)]
    public IAbortSignal Signal { get; protected set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="AbortController"/>.
    /// </summary>
    /// <returns></returns>
    public AbortController() => Signal = new AbortSignal();

    /// <inheritdoc/>
    [ScriptMember]
    public void Abort(Exception? reason)
    {
        Signal.IsAborted = true;
        Signal.Reason = reason ?? new OperationCanceledException("Операция отменена");
        Signal.DispatchEvent(new Event("abort"));
    }

    /// <inheritdoc/>
    [ScriptMember]
    public void Abort() => Abort(default);
}