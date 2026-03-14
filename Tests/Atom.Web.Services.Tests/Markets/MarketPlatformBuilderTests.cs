using Atom.Web.Services.Binance;
using Atom.Web.Services.Coinbase;
using Atom.Web.Services.Kraken;
using Atom.Web.Services.Markets;

namespace Atom.Web.Services.Markets.Tests;

/// <summary>
/// Тесты для MarketPlatformBuilder / MarketPlatformRegistry / MarketPlatformRegistration.
/// </summary>
public class MarketPlatformBuilderTests(ILogger logger) : BenchmarkTests<MarketPlatformBuilderTests>(logger)
{
    public MarketPlatformBuilderTests() : this(ConsoleLogger.Unicode) { }

    #region Builder — базовые операции

    [TestCase(TestName = "MarketPlatformBuilder: Build() с пустым реестром")]
    public void BuildEmptyRegistry()
    {
        var registry = new MarketPlatformBuilder().Build();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(registry.Count, Is.EqualTo(0));
        Assert.That(registry.PlatformNames, Is.Empty);
    }

    [TestCase(TestName = "MarketPlatformBuilder: AddPlatform регистрирует платформу")]
    public void AddPlatformRegisters()
    {
        var registry = new MarketPlatformBuilder()
            .AddPlatform("TestPlatform", new MarketPlatformRegistration
            {
                Name = "TestPlatform",
                ClientFactory = () => new KrakenClient(),
                RestClientFactory = () => new KrakenRestClient()
            })
            .Build();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(registry.Count, Is.EqualTo(1));
        Assert.That(registry.Contains("TestPlatform"), Is.True);
        Assert.That(registry.Contains("Unknown"), Is.False);
    }

    [TestCase(TestName = "MarketPlatformBuilder: регистрация нескольких платформ")]
    public void MultiplePlatforms()
    {
        var registry = new MarketPlatformBuilder()
            .AddPlatform("Kraken", new MarketPlatformRegistration
            {
                Name = "Kraken",
                ClientFactory = () => new KrakenClient()
            })
            .AddPlatform("Coinbase", new MarketPlatformRegistration
            {
                Name = "Coinbase",
                ClientFactory = () => new CoinbaseClient()
            })
            .Build();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(registry.Count, Is.EqualTo(2));
        Assert.That(registry.Contains("Kraken"), Is.True);
        Assert.That(registry.Contains("Coinbase"), Is.True);
    }

    [TestCase(TestName = "MarketPlatformBuilder: case-insensitive поиск платформ")]
    public void CaseInsensitiveLookup()
    {
        var registry = new MarketPlatformBuilder()
            .AddPlatform("Kraken", new MarketPlatformRegistration
            {
                Name = "Kraken",
                ClientFactory = () => new KrakenClient()
            })
            .Build();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(registry.Contains("kraken"), Is.True);
        Assert.That(registry.Contains("KRAKEN"), Is.True);
        Assert.That(registry.Contains("Kraken"), Is.True);
    }

    [TestCase(TestName = "MarketPlatformBuilder: перезапись при повторной регистрации")]
    public void OverwriteRegistration()
    {
        var registry = new MarketPlatformBuilder()
            .AddPlatform("Exchange", new MarketPlatformRegistration
            {
                Name = "Exchange",
                ClientFactory = () => new KrakenClient()
            })
            .AddPlatform("Exchange", new MarketPlatformRegistration
            {
                Name = "Exchange",
                ClientFactory = () => new CoinbaseClient()
            })
            .Build();

        Assert.That(registry.Count, Is.EqualTo(1));
        var client = registry.CreateClient("Exchange");
        Assert.That(client, Is.InstanceOf<CoinbaseClient>());
        client.Dispose();
    }

    #endregion

    #region Builder — обобщённая регистрация

    [TestCase(TestName = "MarketPlatformBuilder: AddPlatform<T,T,T> обобщённая регистрация")]
    public void GenericAddPlatform()
    {
        var registry = new MarketPlatformBuilder()
            .AddPlatform<KrakenClient, KrakenRestClient, KrakenPriceStream>("Kraken")
            .Build();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(registry.Count, Is.EqualTo(1));

        var client = registry.CreateClient("Kraken");
        Assert.That(client, Is.InstanceOf<KrakenClient>());
        client.Dispose();

        var rest = registry.CreateRestClient("Kraken");
        Assert.That(rest, Is.InstanceOf<KrakenRestClient>());
        rest.Dispose();

        var stream = registry.CreatePriceStream("Kraken");
        Assert.That(stream, Is.InstanceOf<KrakenPriceStream>());
        stream.Dispose();
    }

    #endregion

    #region Registry — CreateClient

    [TestCase(TestName = "MarketPlatformRegistry: CreateClient возвращает новый экземпляр")]
    public void CreateClientReturnsNewInstance()
    {
        var registry = new MarketPlatformBuilder()
            .AddPlatform<KrakenClient, KrakenRestClient, KrakenPriceStream>("Kraken")
            .Build();

        using var client1 = registry.CreateClient("Kraken");
        using var client2 = registry.CreateClient("Kraken");

        Assert.That(client1, Is.Not.SameAs(client2));
    }

    [TestCase(TestName = "MarketPlatformRegistry: CreateClient бросает MarketException для неизвестной платформы")]
    public void CreateClientThrowsForUnknown()
    {
        var registry = new MarketPlatformBuilder().Build();
        Assert.Throws<MarketException>(() => registry.CreateClient("Unknown"));
    }

    [TestCase(TestName = "MarketPlatformRegistry: CreateClient бросает при отсутствии фабрики")]
    public void CreateClientThrowsWithoutFactory()
    {
        var registry = new MarketPlatformBuilder()
            .AddPlatform("NoClient", new MarketPlatformRegistration
            {
                Name = "NoClient",
                RestClientFactory = () => new KrakenRestClient()
            })
            .Build();

        Assert.Throws<MarketException>(() => registry.CreateClient("NoClient"));
    }

    #endregion

    #region Registry — CreateRestClient

    [TestCase(TestName = "MarketPlatformRegistry: CreateRestClient возвращает корректный экземпляр")]
    public void CreateRestClientWorks()
    {
        var registry = new MarketPlatformBuilder()
            .AddPlatform<CoinbaseClient, CoinbaseRestClient, CoinbasePriceStream>("Coinbase")
            .Build();

        using var rest = registry.CreateRestClient("Coinbase");
        Assert.That(rest, Is.InstanceOf<CoinbaseRestClient>());
    }

    [TestCase(TestName = "MarketPlatformRegistry: CreateRestClient бросает MarketException для неизвестной платформы")]
    public void CreateRestClientThrowsForUnknown()
    {
        var registry = new MarketPlatformBuilder().Build();
        Assert.Throws<MarketException>(() => registry.CreateRestClient("Unknown"));
    }

    #endregion

    #region Registry — CreatePriceStream

    [TestCase(TestName = "MarketPlatformRegistry: CreatePriceStream возвращает корректный экземпляр")]
    public void CreatePriceStreamWorks()
    {
        var registry = new MarketPlatformBuilder()
            .AddPlatform<KrakenClient, KrakenRestClient, KrakenPriceStream>("Kraken")
            .Build();

        using var stream = registry.CreatePriceStream("Kraken");
        Assert.That(stream, Is.InstanceOf<KrakenPriceStream>());
        Assert.That(stream.TokenCount, Is.EqualTo(0));
    }

    [TestCase(TestName = "MarketPlatformRegistry: CreatePriceStream бросает для неизвестной платформы")]
    public void CreatePriceStreamThrowsForUnknown()
    {
        var registry = new MarketPlatformBuilder().Build();
        Assert.Throws<MarketException>(() => registry.CreatePriceStream("Unknown"));
    }

    #endregion

    #region Registry — TryCreate

    [TestCase(TestName = "MarketPlatformRegistry: TryCreateClient успех и неудача")]
    public void TryCreateClientSuccessAndFailure()
    {
        var registry = new MarketPlatformBuilder()
            .AddPlatform<KrakenClient, KrakenRestClient, KrakenPriceStream>("Kraken")
            .Build();

        using var scope = Assert.EnterMultipleScope();

        Assert.That(registry.TryCreateClient("Kraken", out var client), Is.True);
        Assert.That(client, Is.InstanceOf<KrakenClient>());
        client!.Dispose();

        Assert.That(registry.TryCreateClient("Unknown", out var noClient), Is.False);
        Assert.That(noClient, Is.Null);
    }

    [TestCase(TestName = "MarketPlatformRegistry: TryCreateRestClient успех и неудача")]
    public void TryCreateRestClientSuccessAndFailure()
    {
        var registry = new MarketPlatformBuilder()
            .AddPlatform<CoinbaseClient, CoinbaseRestClient, CoinbasePriceStream>("Coinbase")
            .Build();

        using var scope = Assert.EnterMultipleScope();

        Assert.That(registry.TryCreateRestClient("Coinbase", out var rest), Is.True);
        Assert.That(rest, Is.InstanceOf<CoinbaseRestClient>());
        rest!.Dispose();

        Assert.That(registry.TryCreateRestClient("Unknown", out _), Is.False);
    }

    [TestCase(TestName = "MarketPlatformRegistry: TryCreatePriceStream успех и неудача")]
    public void TryCreatePriceStreamSuccessAndFailure()
    {
        var registry = new MarketPlatformBuilder()
            .AddPlatform<KrakenClient, KrakenRestClient, KrakenPriceStream>("Kraken")
            .Build();

        using var scope = Assert.EnterMultipleScope();

        Assert.That(registry.TryCreatePriceStream("Kraken", out var stream), Is.True);
        Assert.That(stream, Is.InstanceOf<KrakenPriceStream>());
        stream!.Dispose();

        Assert.That(registry.TryCreatePriceStream("Unknown", out _), Is.False);
    }

    #endregion

    #region Registry — TryGetRegistration

    [TestCase(TestName = "MarketPlatformRegistry: TryGetRegistration")]
    public void TryGetRegistration()
    {
        var registry = new MarketPlatformBuilder()
            .AddPlatform("Kraken", new MarketPlatformRegistration
            {
                Name = "Kraken",
                ClientFactory = () => new KrakenClient()
            })
            .Build();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(registry.TryGetRegistration("Kraken", out var reg), Is.True);
        Assert.That(reg!.Name, Is.EqualTo("Kraken"));
        Assert.That(reg.ClientFactory, Is.Not.Null);

        Assert.That(registry.TryGetRegistration("Unknown", out _), Is.False);
    }

    #endregion

    #region Registry — PlatformNames

    [TestCase(TestName = "MarketPlatformRegistry: PlatformNames содержит все зарегистрированные")]
    public void PlatformNamesContainsAll()
    {
        var registry = new MarketPlatformBuilder()
            .AddPlatform<KrakenClient, KrakenRestClient, KrakenPriceStream>("Kraken")
            .AddPlatform<CoinbaseClient, CoinbaseRestClient, CoinbasePriceStream>("Coinbase")
            .Build();

        var names = registry.PlatformNames.ToList();

        using var scope = Assert.EnterMultipleScope();
        Assert.That(names, Has.Count.EqualTo(2));
        Assert.That(names, Does.Contain("Kraken"));
        Assert.That(names, Does.Contain("Coinbase"));
    }

    #endregion

    #region Валидация параметров

    [TestCase(TestName = "MarketPlatformBuilder: ArgumentException при пустом имени")]
    public void AddPlatformThrowsOnEmptyName()
    {
        var builder = new MarketPlatformBuilder();
        Assert.Throws<ArgumentException>(() =>
            builder.AddPlatform("", new MarketPlatformRegistration { Name = "" }));
    }

    [TestCase(TestName = "MarketPlatformBuilder: ArgumentNullException при null регистрации")]
    public void AddPlatformThrowsOnNullRegistration()
    {
        var builder = new MarketPlatformBuilder();
        Assert.Throws<ArgumentNullException>(() =>
            builder.AddPlatform("Test", null!));
    }

    #endregion
}
