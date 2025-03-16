namespace Atom.Debug.Logging;

/// <summary>
/// Представляет аргументы события для <see cref="ScopeContext"/>.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="ScopeContextEventArgs"/>.
/// </remarks>
/// <param name="scope">Контекст логирования.</param>
public class ScopeContextEventArgs(ScopeContext scope) : EventArgs
{
    /// <summary>
    /// Контекст логирования.
    /// </summary>
    public ScopeContext Scope { get; set; } = scope;
}