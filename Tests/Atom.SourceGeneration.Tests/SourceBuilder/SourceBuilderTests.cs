using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.SourceGeneration.Tests;

public class SourceBuilderTests(ILogger logger) : BenchmarkTest<SourceBuilderTests>(logger)
{
    private static string? sourceReference;

    public override bool IsBenchmarkDisabled => true;

    public SourceBuilderTests() : this(ConsoleLogger.Unicode) { }

    private static void SetUp()
    {
        if (string.IsNullOrEmpty(sourceReference) && File.Exists("assets/source.reference"))
            sourceReference = File.ReadAllText("assets/source.reference");
    }

    public override void GlobalSetUp()
    {
        SetUp();
        base.GlobalSetUp();
    }

    public override void OneTimeSetUp()
    {
        SetUp();
        base.OneTimeSetUp();
    }

    [TestCase(TestName = "Тест сборки исходника"), Benchmark]
    public void SourceTest()
    {
        var src = SourceBuilder.Create()
            .WithNamespace("Test")
            .WithClass(ClassEntity.Create("TestClass", AccessModifier.Public)
                .AsPartial()
                .WithProperty<int>("TestProperty")
                .WithMethod<ValueTask>("OnPropertyChanged")
            )
            .Build(true);

        if (IsTest)
        {
            Assert.That(src, Is.Not.Null);
            Assert.That(src, Is.EqualTo(sourceReference));
        }
    }
}