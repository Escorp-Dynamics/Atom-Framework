using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Конфигурация стратегии для сохранения/загрузки.
/// </summary>
public sealed class PolymarketStrategyConfig
{
    /// <summary>Тип стратегии ("Momentum", "MeanReversion", "Arbitrage").</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Период наблюдения.</summary>
    [JsonPropertyName("lookbackPeriod")]
    public int LookbackPeriod { get; init; }

    /// <summary>Порог (momentum threshold / deviation threshold / spread threshold).</summary>
    [JsonPropertyName("threshold")]
    public double Threshold { get; init; }

    /// <summary>Размер позиции (USDC).</summary>
    [JsonPropertyName("positionSize")]
    public double PositionSize { get; init; }

    /// <summary>Привязанные активы.</summary>
    [JsonPropertyName("assetIds")]
    public string[]? AssetIds { get; init; }

    /// <summary>Пары для арбитража (tokenA → tokenB).</summary>
    [JsonPropertyName("arbitragePairs")]
    public PolymarketArbitragePairConfig[]? ArbitragePairs { get; init; }
}

/// <summary>
/// Конфигурация пары для арбитражной стратегии.
/// </summary>
public sealed class PolymarketArbitragePairConfig
{
    /// <summary>Первый токен.</summary>
    [JsonPropertyName("tokenA")]
    public required string TokenA { get; init; }

    /// <summary>Второй токен.</summary>
    [JsonPropertyName("tokenB")]
    public required string TokenB { get; init; }
}

/// <summary>
/// Конфигурация правила риск-менеджмента.
/// </summary>
public sealed class PolymarketRiskRuleConfig
{
    /// <summary>Идентификатор актива.</summary>
    [JsonPropertyName("assetId")]
    public required string AssetId { get; init; }

    /// <summary>Stop-Loss цена.</summary>
    [JsonPropertyName("stopLossPrice")]
    public double? StopLossPrice { get; init; }

    /// <summary>Take-Profit цена.</summary>
    [JsonPropertyName("takeProfitPrice")]
    public double? TakeProfitPrice { get; init; }

    /// <summary>Trailing stop (%).</summary>
    [JsonPropertyName("trailingStopPercent")]
    public double? TrailingStopPercent { get; init; }

    /// <summary>Макс. убыток на позицию.</summary>
    [JsonPropertyName("maxLossPerPosition")]
    public double? MaxLossPerPosition { get; init; }
}

/// <summary>
/// Конфигурация webhook.
/// </summary>
public sealed class PolymarketWebhookConfigData
{
    /// <summary>Идентификатор.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>URL (HTTPS).</summary>
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary>Тип платформы.</summary>
    [JsonPropertyName("type")]
    public PolymarketWebhookType Type { get; init; }

    /// <summary>Telegram chat_id.</summary>
    [JsonPropertyName("telegramChatId")]
    public string? TelegramChatId { get; init; }

    /// <summary>Активен.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// Конфигурация лимитов портфеля.
/// </summary>
public sealed class PolymarketLimitsConfig
{
    /// <summary>Макс. размер позиции.</summary>
    [JsonPropertyName("maxPositionSize")]
    public double MaxPositionSize { get; init; } = double.MaxValue;

    /// <summary>Макс. количество позиций.</summary>
    [JsonPropertyName("maxOpenPositions")]
    public int MaxOpenPositions { get; init; } = int.MaxValue;

    /// <summary>Макс. убыток портфеля.</summary>
    [JsonPropertyName("maxPortfolioLoss")]
    public double MaxPortfolioLoss { get; init; } = double.MaxValue;

    /// <summary>Макс. доля на один актив (0–1).</summary>
    [JsonPropertyName("maxPositionPercent")]
    public double MaxPositionPercent { get; init; } = 1.0;

    /// <summary>Макс. дневной убыток.</summary>
    [JsonPropertyName("maxDailyLoss")]
    public double MaxDailyLoss { get; init; } = double.MaxValue;
}

/// <summary>
/// Полная конфигурация торговой системы — стратегии, риски, webhooks, лимиты.
/// </summary>
public sealed class PolymarketSystemConfig
{
    /// <summary>Стратегии.</summary>
    [JsonPropertyName("strategies")]
    public PolymarketStrategyConfig[]? Strategies { get; init; }

    /// <summary>Правила рисков.</summary>
    [JsonPropertyName("riskRules")]
    public PolymarketRiskRuleConfig[]? RiskRules { get; init; }

    /// <summary>Webhooks.</summary>
    [JsonPropertyName("webhooks")]
    public PolymarketWebhookConfigData[]? Webhooks { get; init; }

    /// <summary>Лимиты портфеля.</summary>
    [JsonPropertyName("limits")]
    public PolymarketLimitsConfig? Limits { get; init; }

    /// <summary>Минимальная уверенность сигнала.</summary>
    [JsonPropertyName("minConfidence")]
    public double MinConfidence { get; init; } = 0.3;

    /// <summary>DryRun режим.</summary>
    [JsonPropertyName("dryRun")]
    public bool DryRun { get; init; } = true;

    /// <summary>Интервал оценки стратегий (секунды).</summary>
    [JsonPropertyName("evaluationIntervalSeconds")]
    public int EvaluationIntervalSeconds { get; init; } = 30;

    /// <summary>Cooldown между ордерами (секунды).</summary>
    [JsonPropertyName("orderCooldownSeconds")]
    public int OrderCooldownSeconds { get; init; } = 60;
}

/// <summary>
/// Загружает и сохраняет конфигурацию торговой системы Polymarket из/в JSON.
/// </summary>
/// <remarks>
/// Совместим с NativeAOT — использует source-generated JSON.
/// </remarks>
public sealed class PolymarketConfigManager
{
    /// <summary>
    /// Загружает конфигурацию из JSON-строки.
    /// </summary>
    public PolymarketSystemConfig? Load(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);
        return JsonSerializer.Deserialize(json, PolymarketConfigJsonContext.Default.PolymarketSystemConfig);
    }

    /// <summary>
    /// Загружает конфигурацию из файла.
    /// </summary>
    public PolymarketSystemConfig? LoadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        var json = File.ReadAllText(filePath);
        return Load(json);
    }

    /// <summary>
    /// Сохраняет конфигурацию в JSON-строку.
    /// </summary>
    public string Save(PolymarketSystemConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return JsonSerializer.Serialize(config, PolymarketConfigJsonContext.Default.PolymarketSystemConfig);
    }

    /// <summary>
    /// Сохраняет конфигурацию в файл.
    /// </summary>
    public void SaveToFile(PolymarketSystemConfig config, string filePath)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        File.WriteAllText(filePath, Save(config));
    }

    /// <summary>
    /// Создаёт стратегии из конфигурации.
    /// </summary>
    public (IPolymarketStrategy strategy, string[] assetIds)[] CreateStrategies(PolymarketSystemConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.Strategies is null || config.Strategies.Length == 0)
            return [];

        var result = new List<(IPolymarketStrategy, string[])>();

        foreach (var sc in config.Strategies)
        {
            IPolymarketStrategy strategy = sc.Type switch
            {
                "Momentum" => new PolymarketMomentumStrategy(sc.LookbackPeriod, sc.Threshold, sc.PositionSize),
                "MeanReversion" => new PolymarketMeanReversionStrategy(sc.LookbackPeriod, sc.Threshold, sc.PositionSize),
                "Arbitrage" => CreateArbitrageStrategy(sc),
                _ => throw new PolymarketException($"Неизвестный тип стратегии: {sc.Type}")
            };

            result.Add((strategy, sc.AssetIds ?? []));
        }

        return [.. result];
    }

    /// <summary>
    /// Создаёт правила рисков из конфигурации.
    /// </summary>
    public PolymarketRiskRule[] CreateRiskRules(PolymarketSystemConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.RiskRules is null) return [];

        return config.RiskRules.Select(rc => new PolymarketRiskRule
        {
            AssetId = rc.AssetId,
            StopLossPrice = rc.StopLossPrice,
            TakeProfitPrice = rc.TakeProfitPrice,
            TrailingStopPercent = rc.TrailingStopPercent,
            MaxLossPerPosition = rc.MaxLossPerPosition
        }).ToArray();
    }

    /// <summary>
    /// Создаёт webhook-конфигурации из системной конфигурации.
    /// </summary>
    public PolymarketWebhookConfig[] CreateWebhookConfigs(PolymarketSystemConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.Webhooks is null) return [];

        return config.Webhooks.Select(wc => new PolymarketWebhookConfig
        {
            Id = wc.Id,
            Url = wc.Url,
            Type = wc.Type,
            TelegramChatId = wc.TelegramChatId,
            IsEnabled = wc.Enabled
        }).ToArray();
    }

    /// <summary>
    /// Создаёт лимиты из конфигурации.
    /// </summary>
    public PolymarketPortfolioLimits CreateLimits(PolymarketSystemConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.Limits is null) return new();

        return new PolymarketPortfolioLimits
        {
            MaxPositionSize = config.Limits.MaxPositionSize,
            MaxOpenPositions = config.Limits.MaxOpenPositions,
            MaxPortfolioLoss = config.Limits.MaxPortfolioLoss,
            MaxPositionPercent = config.Limits.MaxPositionPercent,
            MaxDailyLoss = config.Limits.MaxDailyLoss
        };
    }

    /// <summary>
    /// Создаёт арбитражную стратегию с парами.
    /// </summary>
    private static PolymarketArbitrageStrategy CreateArbitrageStrategy(PolymarketStrategyConfig sc)
    {
        var strategy = new PolymarketArbitrageStrategy(sc.Threshold, sc.PositionSize);

        if (sc.ArbitragePairs is not null)
        {
            foreach (var pair in sc.ArbitragePairs)
                strategy.RegisterPair(pair.TokenA, pair.TokenB);
        }

        return strategy;
    }
}

/// <summary>
/// Source-generated JSON для конфигурации (NativeAOT-совместимо).
/// </summary>
[JsonSerializable(typeof(PolymarketSystemConfig))]
[JsonSerializable(typeof(PolymarketStrategyConfig))]
[JsonSerializable(typeof(PolymarketStrategyConfig[]))]
[JsonSerializable(typeof(PolymarketRiskRuleConfig))]
[JsonSerializable(typeof(PolymarketRiskRuleConfig[]))]
[JsonSerializable(typeof(PolymarketWebhookConfigData))]
[JsonSerializable(typeof(PolymarketWebhookConfigData[]))]
[JsonSerializable(typeof(PolymarketLimitsConfig))]
[JsonSerializable(typeof(PolymarketArbitragePairConfig))]
[JsonSerializable(typeof(PolymarketArbitragePairConfig[]))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
internal sealed partial class PolymarketConfigJsonContext : JsonSerializerContext;
