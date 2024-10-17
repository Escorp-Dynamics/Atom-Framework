namespace Atom.Web.Browsers.DOM;

/// <summary>
/// Состояние готовности документа.
/// </summary>
public enum DocumentReadyState
{
    /// <summary>
    /// Документ загружается.
    /// </summary>
    Loading,
    /// <summary>
    /// Документ частично загружен и может быть взаимодействием с пользователем.
    /// </summary>
    Interactive,
    /// <summary>
    /// Документ полностью загружен и готов к использованию.
    /// </summary>
    Complete,
}