using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Running;
using NUnit.Framework;

namespace Atom.Testing;

/// <summary>
/// Представляет базовую реализацию тестового модуля с поддержкой замеров производительности.
/// </summary>
/// <typeparam name="T">Тип тестового модуля.</typeparam>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="BenchmarkTests{T}"/>.
/// </remarks>
/// <param name="logger">Журнал событий для бенчмарка.</param>
[SimpleJob(RuntimeMoniker.Net90, baseline: true)]
[SimpleJob(RuntimeMoniker.NativeAot90)]
[TestFixture, MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
#if DEBUG
[Config(typeof(DebugBuildConfig))]
#endif
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public abstract class BenchmarkTests<T>(ILogger logger) : IBenchmarkTests where T : IBenchmarkTests
{
    /// <inheritdoc/>
    public virtual bool IsBenchmarkEnabled { get; set; }

    /// <inheritdoc/>
    public ILogger Logger { get; } = logger;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="BenchmarkTests{T}"/>.
    /// </summary>
    protected BenchmarkTests() : this(ConsoleLogger.Unicode) { }

    /// <inheritdoc/>
    public virtual void GlobalSetUp() => IsBenchmarkEnabled = true;

    /// <inheritdoc/>
    public virtual void OneTimeSetUp() { }

    /// <inheritdoc/>
    public virtual void GlobalTearDown() { }

    /// <inheritdoc/>
    public virtual void OneTimeTearDown() { }

    /// <summary>
    /// Запускает замеры производительности.
    /// </summary>
    [TestCase(TestName = "Замеры производительности")]
    public virtual void RunBenchmarks()
    {
        if (!IsBenchmarkEnabled) return;

        var summary = BenchmarkRunner.Run<T>();
        Assert.That(summary, Is.Not.Null);
    }
}