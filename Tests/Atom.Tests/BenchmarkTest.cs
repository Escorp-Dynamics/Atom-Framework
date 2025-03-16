using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Running;

namespace Atom;

[SimpleJob(RuntimeMoniker.Net90, baseline: true)]
//[SimpleJob(RuntimeMoniker.NativeAot90)]
[TestFixture, MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
#if DEBUG
[Config(typeof(DebugBuildConfig))]
#endif
public abstract class BenchmarkTest<T>(ILogger logger)
{
    public bool IsBenchmark { get; set; }

    public bool IsTest { get; set; } = true;

    public virtual bool IsBenchmarkDisabled { get; set; }

    public ILogger Logger { get; } = logger;

    protected BenchmarkTest() : this(ConsoleLogger.Unicode) { }

    [OneTimeSetUp]
    public virtual void OneTimeSetUp() { }

    [GlobalSetup]
    public virtual void GlobalSetUp()
    {
        IsBenchmark = true;
        IsTest = default;
    }

    [OneTimeTearDown]
    public virtual void OneTimeDispose() { }

    [TearDown]
    public virtual void GlobalDispose() { }

#if BENCHMARKS
    [TestCase(TestName = "Замеры производительности")]
    public virtual void RunBenchmarks()
    {
        if (IsBenchmarkDisabled) return;

        var summary = BenchmarkRunner.Run<T>();
        Assert.That(summary, Is.Not.Null);
    }
#endif
}