using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.Web.Proxies.Services.Tests;

public class ProxyNovaServiceTests(ILogger logger) : BenchmarkTest<ProxyNovaServiceTests>(logger)
{
    public override bool IsBenchmarkDisabled => true;

    public ProxyNovaServiceTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Базовый тест"), Benchmark]
    public async Task BaseTest()
    {
        using var service = new ProxyNovaService();

        var proxy = await service.GetAsync();
        Assert.That(proxy, Is.Not.Null);
    }
}