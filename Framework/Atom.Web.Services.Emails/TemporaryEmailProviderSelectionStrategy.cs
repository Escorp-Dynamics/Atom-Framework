namespace Atom.Web.Emails;

/// <summary>
/// Определяет стратегию выбора следующего провайдера временной почты.
/// </summary>
public enum TemporaryEmailProviderSelectionStrategy
{
    /// <summary>
    /// Последовательно чередует доступных провайдеров.
    /// </summary>
    RoundRobin = 0,

    /// <summary>
    /// Случайно перемешивает доступных провайдеров перед попыткой создания аккаунта.
    /// </summary>
    Random = 1,
}