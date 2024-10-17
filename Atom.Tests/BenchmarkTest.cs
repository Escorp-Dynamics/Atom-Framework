using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Running;

namespace Atom;

[SimpleJob(RuntimeMoniker.Net80, baseline: true)]
[SimpleJob(RuntimeMoniker.NativeAot80)]
[TestFixture, MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
#if DEBUG
[Config(typeof(DebugBuildConfig))]
#endif
public abstract class BenchmarkTest<T>(ILogger logger)
{
    private readonly ILogger logger = logger;
    
    public bool IsBenchmark { get; set; }

    public bool IsTest { get; set; } = true;

    public virtual bool IsBenchmarkDisabled { get; set; }

    public ILogger Logger => logger;

    public BenchmarkTest() : this(ConsoleLogger.Unicode) { }

    [OneTimeSetUp]
    public virtual void OneTimeSetUp() { }

    [GlobalSetup]
    public virtual void GlobalSetUp()
    {
        IsBenchmark = true;
        IsTest = default;
    }

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