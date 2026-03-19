using System.Collections.Concurrent;
using System.Globalization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Профиль портфеля — обёртка над <see cref="PolymarketPortfolioTracker"/> с именем и стратегией.
/// </summary>
public sealed class PolymarketPortfolioProfile
{
    /// <summary>
    /// Уникальный идентификатор портфеля.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Отображаемое имя портфеля.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Название стратегии (например, "Market Making", "Momentum", "Hedging").
    /// </summary>
    public string? Strategy { get; init; }

    /// <summary>
    /// Произвольные теги для группировки.
    /// </summary>
    public string[]? Tags { get; init; }

    /// <summary>
    /// Трекер портфеля с позициями.
    /// </summary>
    public required PolymarketPortfolioTracker Tracker { get; init; }

    /// <summary>
    /// История P&amp;L для данного портфеля (null, если не включена).
    /// </summary>
    public PolymarketPnLHistory? PnLHistory { get; init; }

    /// <summary>
    /// Время создания профиля (UTC).
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Аргументы события изменения в мульти-портфеле.
/// </summary>
public sealed class PolymarketPortfolioEventArgs(PolymarketPortfolioProfile profile) : EventArgs
{
    /// <summary>
    /// Профиль портфеля.
    /// </summary>
    public PolymarketPortfolioProfile Profile { get; } = profile;
}

/// <summary>
/// Агрегированная статистика по всем портфелям.
/// </summary>
public sealed class PolymarketAggregatedSummary
{
    /// <summary>
    /// Количество активных портфелей.
    /// </summary>
    public int PortfolioCount { get; init; }

    /// <summary>
    /// Суммарное количество открытых позиций по всем портфелям.
    /// </summary>
    public int TotalOpenPositions { get; init; }

    /// <summary>
    /// Суммарная рыночная стоимость всех позиций.
    /// </summary>
    public double TotalMarketValue { get; init; }

    /// <summary>
    /// Суммарная стоимость входа.
    /// </summary>
    public double TotalCostBasis { get; init; }

    /// <summary>
    /// Суммарный нереализованный P&amp;L.
    /// </summary>
    public double TotalUnrealizedPnL { get; init; }

    /// <summary>
    /// Суммарный реализованный P&amp;L.
    /// </summary>
    public double TotalRealizedPnL { get; init; }

    /// <summary>
    /// Суммарные комиссии.
    /// </summary>
    public double TotalFees { get; init; }

    /// <summary>
    /// Чистый P&amp;L = Realized + Unrealized - Fees.
    /// </summary>
    public double NetPnL => TotalRealizedPnL + TotalUnrealizedPnL - TotalFees;

    /// <summary>
    /// Статистика по каждому портфелю.
    /// </summary>
    public required IReadOnlyDictionary<string, PolymarketPortfolioSummary> PerPortfolio { get; init; }
}

/// <summary>
/// Менеджер множества портфелей Polymarket.
/// Управляет несколькими портфельными трекерами с общей инфраструктурой
/// (WebSocket, PriceStream, EventResolver, AlertSystem).
/// </summary>
/// <remarks>
/// Совместим с NativeAOT. Потокобезопасен.
/// Каждый портфель изолирован по позициям, но использует общий поток данных.
/// </remarks>
public sealed class PolymarketPortfolioManager : IAsyncDisposable, IDisposable
{
    private readonly PolymarketClient client;
    private readonly PolymarketPriceStream priceStream;
    private readonly PolymarketEventResolver resolver;
    private readonly PolymarketAlertSystem alertSystem;
    private readonly bool disposeInfrastructure;
    private readonly ConcurrentDictionary<string, PolymarketPortfolioProfile> portfolios = new();
    private bool isDisposed;

    /// <summary>
    /// Событие при добавлении нового портфеля.
    /// </summary>
    public event AsyncEventHandler<PolymarketPortfolioManager, PolymarketPortfolioEventArgs>? PortfolioAdded;

    /// <summary>
    /// Событие при удалении портфеля.
    /// </summary>
    public event AsyncEventHandler<PolymarketPortfolioManager, PolymarketPortfolioEventArgs>? PortfolioRemoved;

    /// <summary>
    /// Инициализирует менеджер с собственной инфраструктурой.
    /// Создаёт новые WebSocket, PriceStream, EventResolver и AlertSystem.
    /// </summary>
    public PolymarketPortfolioManager()
    {
        client = new PolymarketClient();
        priceStream = new PolymarketPriceStream(client);
        resolver = new PolymarketEventResolver();
        alertSystem = new PolymarketAlertSystem();
        disposeInfrastructure = true;
    }

    /// <summary>
    /// Инициализирует менеджер с предоставленной инфраструктурой.
    /// </summary>
    /// <param name="client">Общий WebSocket-клиент.</param>
    /// <param name="priceStream">Общий поток цен.</param>
    /// <param name="resolver">Общий EventResolver.</param>
    /// <param name="alertSystem">Общая система алертов.</param>
    public PolymarketPortfolioManager(
        PolymarketClient client,
        PolymarketPriceStream priceStream,
        PolymarketEventResolver resolver,
        PolymarketAlertSystem alertSystem)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(priceStream);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(alertSystem);

        this.client = client;
        this.priceStream = priceStream;
        this.resolver = resolver;
        this.alertSystem = alertSystem;
        disposeInfrastructure = false;
    }

    /// <summary>
    /// Общий WebSocket-клиент.
    /// </summary>
    public PolymarketClient Client => client;

    /// <summary>
    /// Общий поток цен.
    /// </summary>
    public PolymarketPriceStream PriceStream => priceStream;

    /// <summary>
    /// Общий мониторинг разрешений рынков.
    /// </summary>
    public PolymarketEventResolver Resolver => resolver;

    /// <summary>
    /// Общая система алертов.
    /// </summary>
    public PolymarketAlertSystem AlertSystem => alertSystem;

    /// <summary>
    /// Все зарегистрированные портфели.
    /// </summary>
    public IReadOnlyDictionary<string, PolymarketPortfolioProfile> Portfolios => portfolios;

    /// <summary>
    /// Количество портфелей.
    /// </summary>
    public int Count => portfolios.Count;

    /// <summary>
    /// Создаёт новый портфель с указанным именем и стратегией.
    /// </summary>
    /// <param name="id">Уникальный идентификатор портфеля.</param>
    /// <param name="name">Отображаемое имя.</param>
    /// <param name="strategy">Название стратегии (необязательно).</param>
    /// <param name="tags">Теги для группировки (необязательно).</param>
    /// <param name="enablePnLHistory">Включить запись P&amp;L истории.</param>
    /// <param name="pnlSnapshotInterval">Интервал записи P&amp;L снимков (по умолчанию 5 минут).</param>
    /// <returns>Созданный профиль портфеля.</returns>
    public PolymarketPortfolioProfile CreatePortfolio(
        string id,
        string name,
        string? strategy = null,
        string[]? tags = null,
        bool enablePnLHistory = false,
        TimeSpan pnlSnapshotInterval = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var tracker = new PolymarketPortfolioTracker(client, priceStream);
        tracker.ConnectResolver(resolver);
        alertSystem.ConnectTracker(tracker);

        PolymarketPnLHistory? pnlHistory = null;
        if (enablePnLHistory)
        {
            pnlHistory = new PolymarketPnLHistory(tracker, pnlSnapshotInterval);
            pnlHistory.Start();
        }

        var profile = new PolymarketPortfolioProfile
        {
            Id = id,
            Name = name,
            Strategy = strategy,
            Tags = tags,
            Tracker = tracker,
            PnLHistory = pnlHistory
        };

        if (!portfolios.TryAdd(id, profile))
            throw new InvalidOperationException($"Портфель с ID '{id}' уже существует.");

        PortfolioAdded?.Invoke(this, new PolymarketPortfolioEventArgs(profile));
        return profile;
    }

    /// <summary>
    /// Удаляет портфель и освобождает его ресурсы.
    /// </summary>
    /// <param name="id">Идентификатор портфеля.</param>
    public async ValueTask RemovePortfolioAsync(string id)
    {
        if (!portfolios.TryRemove(id, out var profile))
            return;

        if (profile.PnLHistory is not null)
            await profile.PnLHistory.DisposeAsync().ConfigureAwait(false);

        profile.Tracker.DisconnectResolver(resolver);
        await profile.Tracker.DisposeAsync().ConfigureAwait(false);

        PortfolioRemoved?.Invoke(this, new PolymarketPortfolioEventArgs(profile));
    }

    /// <summary>
    /// Получает профиль портфеля по идентификатору.
    /// </summary>
    /// <param name="id">Идентификатор портфеля.</param>
    public PolymarketPortfolioProfile? GetPortfolio(string id) =>
        portfolios.TryGetValue(id, out var profile) ? profile : null;

    /// <summary>
    /// Фильтрует портфели по стратегии.
    /// </summary>
    /// <param name="strategy">Название стратегии.</param>
    public PolymarketPortfolioProfile[] GetPortfoliosByStrategy(string strategy) =>
        portfolios.Values.Where(p => p.Strategy == strategy).ToArray();

    /// <summary>
    /// Фильтрует портфели по тегу.
    /// </summary>
    /// <param name="tag">Тег для фильтрации.</param>
    public PolymarketPortfolioProfile[] GetPortfoliosByTag(string tag) =>
        portfolios.Values.Where(p => p.Tags is not null && Array.Exists(p.Tags, t => t == tag)).ToArray();

    /// <summary>
    /// Формирует агрегированную статистику по всем портфелям.
    /// </summary>
    public PolymarketAggregatedSummary GetAggregatedSummary()
    {
        var perPortfolio = new Dictionary<string, PolymarketPortfolioSummary>();
        var totalOpen = 0;
        var totalMarketValue = 0.0;
        var totalCostBasis = 0.0;
        var totalUnrealized = 0.0;
        var totalRealized = 0.0;
        var totalFees = 0.0;

        foreach (var kvp in portfolios)
        {
            var summary = kvp.Value.Tracker.GetSummary();
            perPortfolio[kvp.Key] = summary;

            totalOpen += summary.OpenPositions;
            totalMarketValue += summary.TotalMarketValue;
            totalCostBasis += summary.TotalCostBasis;
            totalUnrealized += summary.TotalUnrealizedPnL;
            totalRealized += summary.TotalRealizedPnL;
            totalFees += summary.TotalFees;
        }

        return new PolymarketAggregatedSummary
        {
            PortfolioCount = portfolios.Count,
            TotalOpenPositions = totalOpen,
            TotalMarketValue = totalMarketValue,
            TotalCostBasis = totalCostBasis,
            TotalUnrealizedPnL = totalUnrealized,
            TotalRealizedPnL = totalRealized,
            TotalFees = totalFees,
            PerPortfolio = perPortfolio
        };
    }

    /// <summary>
    /// Загружает сделки из REST API в указанный портфель.
    /// </summary>
    /// <param name="portfolioId">Идентификатор портфеля.</param>
    /// <param name="restClient">REST-клиент.</param>
    /// <param name="market">Фильтр по рынку (необязательно).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public async ValueTask SyncPortfolioAsync(
        string portfolioId,
        PolymarketRestClient restClient,
        string? market = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(portfolioId);
        ArgumentNullException.ThrowIfNull(restClient);

        if (!portfolios.TryGetValue(portfolioId, out var profile))
            throw new InvalidOperationException($"Портфель '{portfolioId}' не найден.");

        await profile.Tracker.SyncFromRestAsync(restClient, market, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Добавляет алерт для конкретного портфеля.
    /// </summary>
    /// <param name="portfolioId">Идентификатор портфеля.</param>
    /// <param name="alert">Определение алерта.</param>
    public void AddPortfolioAlert(string portfolioId, PolymarketAlertDefinition alert)
    {
        ArgumentException.ThrowIfNullOrEmpty(portfolioId);
        ArgumentNullException.ThrowIfNull(alert);

        if (!portfolios.ContainsKey(portfolioId))
            throw new InvalidOperationException($"Портфель '{portfolioId}' не найден.");

        alertSystem.AddAlert(alert);
    }

    /// <summary>
    /// Начинает мониторинг разрешения рынков.
    /// </summary>
    public void StartResolverPolling() => resolver.StartPolling();

    /// <summary>
    /// Останавливает мониторинг разрешения рынков.
    /// </summary>
    public async ValueTask StopResolverPollingAsync() =>
        await resolver.StopPollingAsync().ConfigureAwait(false);

    /// <summary>
    /// Освобождает все ресурсы асинхронно.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;

        // Удаляем все портфели
        foreach (var kvp in portfolios)
        {
            if (kvp.Value.PnLHistory is not null)
                await kvp.Value.PnLHistory.DisposeAsync().ConfigureAwait(false);

            kvp.Value.Tracker.DisconnectResolver(resolver);
            await kvp.Value.Tracker.DisposeAsync().ConfigureAwait(false);
        }
        portfolios.Clear();

        if (disposeInfrastructure)
        {
            alertSystem.Dispose();
            await resolver.DisposeAsync().ConfigureAwait(false);
            await priceStream.DisposeAsync().ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Освобождает все ресурсы синхронно.
    /// </summary>
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        foreach (var kvp in portfolios)
        {
            kvp.Value.PnLHistory?.Dispose();
            kvp.Value.Tracker.DisconnectResolver(resolver);
            kvp.Value.Tracker.Dispose();
        }
        portfolios.Clear();

        if (disposeInfrastructure)
        {
            alertSystem.Dispose();
            resolver.Dispose();
            priceStream.Dispose();
            client.Dispose();
        }
    }
}
