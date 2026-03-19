namespace Atom.Web.Services.Markets;

/// <summary>
/// Система алертов — мониторинг условий и уведомления.
/// </summary>
public interface IMarketAlertSystem : IDisposable
{
    /// <summary>Количество активных алертов.</summary>
    int ActiveCount { get; }

    /// <summary>
    /// Добавляет алерт.
    /// </summary>
    void AddAlert(IMarketAlertDefinition alert);

    /// <summary>
    /// Удаляет алерт.
    /// </summary>
    void RemoveAlert(string alertId);

    /// <summary>
    /// Получает алерт по идентификатору.
    /// </summary>
    IMarketAlertDefinition? GetAlert(string alertId);

    /// <summary>
    /// Очищает все алерты.
    /// </summary>
    void ClearAlerts();

    /// <summary>
    /// Отключает все подписки.
    /// </summary>
    void DisconnectAll();
}
