using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Atom.Web.Services.Markets;

// ═══════════════════════════════════════════════════════════════════
// DI-регистрация рыночных платформ.
// Предоставляет builder-паттерн для регистрации и резолва
// IMarketClient, IMarketRestClient, IMarketPriceStream
// по имени платформы. NativeAOT-совместимо.
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Регистрация рыночной платформы: фабрики для создания клиентов.
/// </summary>
public sealed class MarketPlatformRegistration
{
    /// <summary>
    /// Имя платформы (напр. "Polymarket", "Binance").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Фабрика создания WebSocket-клиента.
    /// </summary>
    public Func<IMarketClient>? ClientFactory { get; init; }

    /// <summary>
    /// Фабрика создания REST-клиента.
    /// </summary>
    public Func<IMarketRestClient>? RestClientFactory { get; init; }

    /// <summary>
    /// Фабрика создания потока цен.
    /// </summary>
    public Func<IMarketPriceStream>? PriceStreamFactory { get; init; }
}

/// <summary>
/// Построитель реестра рыночных платформ.
/// </summary>
/// <remarks>
/// Пример использования:
/// <code>
/// var registry = new MarketPlatformBuilder()
///     .AddPlatform("Polymarket", new MarketPlatformRegistration
///     {
///         Name = "Polymarket",
///         ClientFactory = () =&gt; new PolymarketClient(key, secret, passphrase),
///         RestClientFactory = () =&gt; new PolymarketRestClient(),
///         PriceStreamFactory = () =&gt; new PolymarketPriceStream()
///     })
///     .AddPlatform("Binance", new MarketPlatformRegistration
///     {
///         Name = "Binance",
///         ClientFactory = () =&gt; new BinanceClient(),
///         RestClientFactory = () =&gt; new BinanceRestClient()
///     })
///     .Build();
///
/// IMarketClient client = registry.CreateClient("Polymarket");
/// IMarketRestClient rest = registry.CreateRestClient("Binance");
/// </code>
/// </remarks>
public sealed class MarketPlatformBuilder
{
    private readonly Dictionary<string, MarketPlatformRegistration> registrations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Регистрирует платформу.
    /// </summary>
    /// <param name="name">Уникальное имя платформы.</param>
    /// <param name="registration">Конфигурация фабрик платформы.</param>
    /// <returns>Текущий <see cref="MarketPlatformBuilder"/> для цепочки вызовов.</returns>
    public MarketPlatformBuilder AddPlatform(string name, MarketPlatformRegistration registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(registration);
        registrations[name] = registration;
        return this;
    }

    /// <summary>
    /// Регистрирует платформу через обобщённые типы.
    /// </summary>
    /// <typeparam name="TClient">Тип WebSocket-клиента (должен иметь конструктор без параметров).</typeparam>
    /// <typeparam name="TRest">Тип REST-клиента (должен иметь конструктор без параметров).</typeparam>
    /// <typeparam name="TPriceStream">Тип потока цен (должен иметь конструктор без параметров).</typeparam>
    /// <param name="name">Уникальное имя платформы.</param>
    /// <returns>Текущий <see cref="MarketPlatformBuilder"/> для цепочки вызовов.</returns>
    public MarketPlatformBuilder AddPlatform<TClient, TRest, TPriceStream>(string name)
        where TClient : IMarketClient, new()
        where TRest : IMarketRestClient, new()
        where TPriceStream : IMarketPriceStream, new()
    {
        return AddPlatform(name, new MarketPlatformRegistration
        {
            Name = name,
            ClientFactory = static () => new TClient(),
            RestClientFactory = static () => new TRest(),
            PriceStreamFactory = static () => new TPriceStream()
        });
    }

    /// <summary>
    /// Создаёт неизменяемый реестр платформ.
    /// </summary>
    /// <returns>Экземпляр <see cref="MarketPlatformRegistry"/>.</returns>
    public MarketPlatformRegistry Build()
    {
        return new MarketPlatformRegistry(registrations.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Неизменяемый реестр зарегистрированных рыночных платформ.
/// Потокобезопасен (FrozenDictionary).
/// </summary>
public sealed class MarketPlatformRegistry
{
    private readonly FrozenDictionary<string, MarketPlatformRegistration> platforms;

    internal MarketPlatformRegistry(FrozenDictionary<string, MarketPlatformRegistration> platforms)
    {
        this.platforms = platforms;
    }

    /// <summary>
    /// Список зарегистрированных платформ.
    /// </summary>
    public IEnumerable<string> PlatformNames => platforms.Keys;

    /// <summary>
    /// Количество зарегистрированных платформ.
    /// </summary>
    public int Count => platforms.Count;

    /// <summary>
    /// Проверяет, зарегистрирована ли платформа.
    /// </summary>
    public bool Contains(string name) => platforms.ContainsKey(name);

    /// <summary>
    /// Пытается получить регистрацию платформы.
    /// </summary>
    public bool TryGetRegistration(string name, [NotNullWhen(true)] out MarketPlatformRegistration? registration)
        => platforms.TryGetValue(name, out registration);

    /// <summary>
    /// Создаёт новый экземпляр WebSocket-клиента для указанной платформы.
    /// </summary>
    /// <param name="name">Имя платформы.</param>
    /// <returns>Экземпляр <see cref="IMarketClient"/>.</returns>
    /// <exception cref="MarketException">Платформа не зарегистрирована или фабрика не задана.</exception>
    public IMarketClient CreateClient(string name)
    {
        if (!platforms.TryGetValue(name, out var reg) || reg.ClientFactory is null)
            throw new MarketException($"Платформа '{name}' не зарегистрирована или ClientFactory не задан.",
                new InvalidOperationException(name));
        return reg.ClientFactory();
    }

    /// <summary>
    /// Создаёт новый экземпляр REST-клиента для указанной платформы.
    /// </summary>
    /// <param name="name">Имя платформы.</param>
    /// <returns>Экземпляр <see cref="IMarketRestClient"/>.</returns>
    /// <exception cref="MarketException">Платформа не зарегистрирована или фабрика не задана.</exception>
    public IMarketRestClient CreateRestClient(string name)
    {
        if (!platforms.TryGetValue(name, out var reg) || reg.RestClientFactory is null)
            throw new MarketException($"Платформа '{name}' не зарегистрирована или RestClientFactory не задан.",
                new InvalidOperationException(name));
        return reg.RestClientFactory();
    }

    /// <summary>
    /// Создаёт новый экземпляр потока цен для указанной платформы.
    /// </summary>
    /// <param name="name">Имя платформы.</param>
    /// <returns>Экземпляр <see cref="IMarketPriceStream"/>.</returns>
    /// <exception cref="MarketException">Платформа не зарегистрирована или фабрика не задана.</exception>
    public IMarketPriceStream CreatePriceStream(string name)
    {
        if (!platforms.TryGetValue(name, out var reg) || reg.PriceStreamFactory is null)
            throw new MarketException($"Платформа '{name}' не зарегистрирована или PriceStreamFactory не задан.",
                new InvalidOperationException(name));
        return reg.PriceStreamFactory();
    }

    /// <summary>
    /// Пытается создать WebSocket-клиент.
    /// </summary>
    public bool TryCreateClient(string name, [NotNullWhen(true)] out IMarketClient? client)
    {
        client = null;
        if (!platforms.TryGetValue(name, out var reg) || reg.ClientFactory is null) return false;
        client = reg.ClientFactory();
        return true;
    }

    /// <summary>
    /// Пытается создать REST-клиент.
    /// </summary>
    public bool TryCreateRestClient(string name, [NotNullWhen(true)] out IMarketRestClient? restClient)
    {
        restClient = null;
        if (!platforms.TryGetValue(name, out var reg) || reg.RestClientFactory is null) return false;
        restClient = reg.RestClientFactory();
        return true;
    }

    /// <summary>
    /// Пытается создать поток цен.
    /// </summary>
    public bool TryCreatePriceStream(string name, [NotNullWhen(true)] out IMarketPriceStream? priceStream)
    {
        priceStream = null;
        if (!platforms.TryGetValue(name, out var reg) || reg.PriceStreamFactory is null) return false;
        priceStream = reg.PriceStreamFactory();
        return true;
    }
}
