namespace Atom.Web.Services.Markets;

/// <summary>
/// Авто-исполнитель ордеров — связывает стратегии с REST-клиентом.
/// </summary>
public interface IMarketOrderExecutor : IAsyncDisposable
{
    /// <summary>DryRun режим (без реальных ордеров).</summary>
    bool DryRun { get; set; }

    /// <summary>Минимальная уверенность сигнала для исполнения.</summary>
    double MinConfidence { get; set; }

    /// <summary>Интервал оценки стратегий.</summary>
    TimeSpan EvaluationInterval { get; set; }

    /// <summary>Cooldown между ордерами по одному активу.</summary>
    TimeSpan OrderCooldown { get; set; }

    /// <summary>
    /// Добавляет стратегию с привязкой к активам.
    /// </summary>
    /// <param name="strategy">Стратегия.</param>
    /// <param name="assetIds">Активы для оценки.</param>
    void AddStrategy(IMarketStrategy strategy, string[] assetIds);

    /// <summary>
    /// Удаляет стратегию.
    /// </summary>
    void RemoveStrategy(string strategyName);

    /// <summary>
    /// Запускает фоновый цикл оценки.
    /// </summary>
    void Start();

    /// <summary>
    /// Останавливает цикл.
    /// </summary>
    ValueTask StopAsync();

    /// <summary>
    /// Выполняет одиночную оценку всех стратегий.
    /// </summary>
    ValueTask EvaluateOnceAsync(CancellationToken cancellationToken = default);
}
