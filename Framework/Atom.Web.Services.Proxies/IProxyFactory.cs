using Atom.Architect.Components;
using Atom.Architect.Factories;
using Atom.Net.Proxies;
using Atom.Web.Analytics;
using Atom.Web.Proxies.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;

namespace Atom.Web.Proxies;

/// <summary>
/// Представляет базовый интерфейс для реализации фабрики прокси-провайдеров.
/// </summary>
public interface IProxyFactory<TProxyProvider, out TProxyFactory> : IComponentOwner<TProxyFactory>, IAsyncFactory<ServiceProxy>, IDisposable
    where TProxyProvider : IProxyProvider
    where TProxyFactory : IProxyFactory<TProxyProvider, TProxyFactory>
{
    /// <summary>
    /// Resolver, определяющий dedup key для aggregate proxy pool.
    /// </summary>
    IProxyDedupKeyResolver DedupKeyResolver { get; set; }
    
    /// <summary>
    /// Логгер фабрики для runtime-диагностики refresh и rebuild процессов.
    /// </summary>
    ILogger? Logger { get; set; }
    
    /// <summary>
    /// Источник Meter для диагностики и внешней инструментализации фабрики.
    /// </summary>
    IMeterFactory? MeterFactory { get; set; }
    
    /// <summary>
    /// Число deduped proxy в последнем перестроенном aggregate snapshot, соответствующих активным factory filters.
    /// Значение обновляется после фонового rebuild и не гарантирует мгновенную синхронность сразу после смены filter-ов или provider stack.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Коллекция подключенных провайдеров.
    /// </summary>
    IEnumerable<TProxyProvider> Providers { get; }

    /// <summary>
    /// Разрешённые протоколы aggregate pool. Пустой набор разрешает все протоколы.
    /// </summary>
    IEnumerable<ProxyType> AllowedProtocols { get; set; }

    /// <summary>
    /// Разрешённые страны. Пустой набор разрешает все страны.
    /// </summary>
    IEnumerable<Country> AllowedCountries { get; set; }

    /// <summary>
    /// Разрешённые уровни анонимности aggregate pool. Пустой набор разрешает все уровни.
    /// </summary>
    IEnumerable<AnonymityLevel> AllowedAnonymityLevels { get; set; }

    /// <summary>
    /// Интервал, через который фабрика обновляет снимки всех подключённых провайдеров.
    /// </summary>
    TimeSpan RefreshInterval { get; set; }

    /// <summary>
    /// Интервал ожидания перед повторным фоновым обновлением после ошибки.
    /// </summary>
    TimeSpan RefreshErrorBackoff { get; set; }

    /// <summary>
    /// Указывает, должна ли фабрика сохранять последний успешный snapshot при ошибке обновления провайдера.
    /// </summary>
    bool PreservePoolOnRefreshFailure { get; set; }

    /// <summary>
    /// Стратегия выбора следующего прокси из объединённого контейнерного пула.
    /// </summary>
    ProxyRotationStrategy RotationStrategy { get; set; }

    /// <summary>
    /// Интервал, после которого невозвращённые leased proxy снимаются с блокировки автоматически.
    /// </summary>
    TimeSpan BlockedLeaseTimeout { get; set; }

    /// <summary>
    /// Возвращает следующий прокси из общего контейнерного пула, удовлетворяющий фильтру.
    /// </summary>
    ValueTask<ServiceProxy> GetAsync(Func<ServiceProxy, bool> filter, CancellationToken cancellationToken);

    /// <summary>
    /// Возвращает следующий прокси из общего контейнерного пула, удовлетворяющий фильтру.
    /// </summary>
    ValueTask<ServiceProxy> GetAsync(Func<ServiceProxy, bool> filter) => GetAsync(filter, CancellationToken.None);

    /// <summary>
    /// Возвращает следующую последовательность прокси из общего контейнерного пула.
    /// </summary>
    ValueTask<IEnumerable<ServiceProxy>> GetAsync(int count, CancellationToken cancellationToken);

    /// <summary>
    /// Возвращает следующую последовательность прокси из общего контейнерного пула.
    /// </summary>
    ValueTask<IEnumerable<ServiceProxy>> GetAsync(int count) => GetAsync(count, CancellationToken.None);

    /// <summary>
    /// Возвращает следующую последовательность прокси из общего контейнерного пула, удовлетворяющую фильтру.
    /// </summary>
    ValueTask<IEnumerable<ServiceProxy>> GetAsync(int count, Func<ServiceProxy, bool> filter, CancellationToken cancellationToken);

    /// <summary>
    /// Возвращает следующую последовательность прокси из общего контейнерного пула, удовлетворяющую фильтру.
    /// </summary>
    ValueTask<IEnumerable<ServiceProxy>> GetAsync(int count, Func<ServiceProxy, bool> filter)
        => GetAsync(count, filter, CancellationToken.None);

    /// <summary>
    /// Вручную очищает утёкшие lease для указанных прокси.
    /// </summary>
    int CleanupLeasedProxies(IEnumerable<ServiceProxy> proxies);

    /// <summary>
    /// Вручную очищает все утёкшие lease.
    /// </summary>
    int CleanupLeasedProxies();
}