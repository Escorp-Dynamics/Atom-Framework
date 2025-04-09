namespace Atom.Web.Proxies.Services.Tests;

public class ProxyNovaServiceTests(ILogger logger) : BenchmarkTests<ProxyNovaServiceTests>(logger)
{
    public ProxyNovaServiceTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Базовый тест"), Benchmark]
    public async Task BaseTest()
    {
        using var service = new ProxyNovaService();

        var proxy = await service.GetAsync();
        Assert.That(proxy, Is.Not.Null);
    }
}