namespace Atom.Web.Browsing;

/// <summary>
/// Состояние готовности контекста просмотра.
/// </summary>
public enum ReadinessState
{
    /// <summary>
    /// Возврат немедленно без проверки состояния готовности.
    /// </summary>
    None,
    /// <summary>
    /// Возврат после того, как состояние готовности станет "interactive".
    /// </summary>
    Interactive,
    /// <summary>
    /// Возврат после того, как состояние готовности станет "complete".
    /// </summary>
    Complete,
}