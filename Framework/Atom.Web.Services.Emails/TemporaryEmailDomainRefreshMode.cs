namespace Atom.Web.Emails;

/// <summary>
/// Определяет стратегию обновления списка доступных доменов временной почты.
/// </summary>
public enum TemporaryEmailDomainRefreshMode
{
    /// <summary>
    /// Запрашивать домены только когда локальный cache ещё пуст.
    /// </summary>
    WhenEmpty = 0,

    /// <summary>
    /// Перед каждым созданием аккаунта запрашивать свежий список доменов.
    /// </summary>
    Always = 1,
}