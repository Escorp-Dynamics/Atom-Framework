using Microsoft.ClearScript;

namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Представляет объект контроллера, который позволяет вам при необходимости обрывать один и более DOM запросов.
/// </summary>
public interface IAbortController
{
    /// <summary>
    /// Экземпляр <see cref="IAbortSignal"/>, который может быть использован для коммуникаций/остановки DOM запросов.
    /// </summary>
    [ScriptMember(ScriptAccess.ReadOnly)]
    IAbortSignal Signal { get; }

    /// <summary>
    /// Прерывает DOM запрос до момента его завершения.
    /// </summary>
    /// <param name="reason">Причина прерывания.</param>
    [ScriptMember]
    void Abort(Exception? reason);

    /// <summary>
    /// Прерывает DOM запрос до момента его завершения.
    /// </summary>
    [ScriptMember]
    void Abort() => Abort(default);
}