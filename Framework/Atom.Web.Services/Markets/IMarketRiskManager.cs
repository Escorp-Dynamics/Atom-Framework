namespace Atom.Web.Services.Markets;

/// <summary>
/// Менеджер рисков — Stop-Loss, Take-Profit, Trailing Stop, позиционные лимиты.
/// </summary>
public interface IMarketRiskManager : IDisposable
{
    /// <summary>Лимиты портфеля.</summary>
    IMarketPortfolioLimits Limits { get; }

    /// <summary>Режим авто-исполнения.</summary>
    bool AutoExecute { get; set; }

    /// <summary>Накопленный дневной убыток.</summary>
    double DailyLoss { get; }

    /// <summary>
    /// Добавляет правило для актива.
    /// </summary>
    void AddRule(IMarketRiskRule rule);

    /// <summary>
    /// Удаляет правило.
    /// </summary>
    void RemoveRule(string assetId);

    /// <summary>
    /// Получает правило для актива.
    /// </summary>
    IMarketRiskRule? GetRule(string assetId);

    /// <summary>
    /// Очищает все правила.
    /// </summary>
    void ClearRules();

    /// <summary>
    /// Проверяет, можно ли открыть позицию.
    /// </summary>
    /// <param name="assetId">Идентификатор актива.</param>
    /// <param name="quantity">Объём.</param>
    bool CanOpenPosition(string assetId, double quantity);

    /// <summary>
    /// Проверяет все правила по текущим ценам.
    /// </summary>
    ValueTask CheckAllRulesAsync();

    /// <summary>
    /// Сбрасывает дневной счётчик убытков.
    /// </summary>
    void ResetDailyLoss();
}
