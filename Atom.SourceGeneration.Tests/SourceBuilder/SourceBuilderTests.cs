using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.SourceGeneration.Tests;

public class SourceBuilderTests(ILogger logger) : BenchmarkTest<SourceBuilderTests>(logger)
{
    private static string? classReference;

    public override bool IsBenchmarkDisabled => true;

    public SourceBuilderTests() : this(ConsoleLogger.Unicode) { }

    private static void SetUp()
    {
        if (string.IsNullOrEmpty(classReference) && File.Exists("assets/source.reference"))
            classReference = File.ReadAllText("assets/source.reference");
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
    public void ClassTest()
    {
        var src = SourceBuilder.Create()
            .WithNamespace("Test")
            .AddClass(ClassEntity.Create("TestClass", AccessModifier.Public)
                .AsPartial()
                .AddProperty<int>("TestProperty")
                .AddMethod<ValueTask>("OnPropertyChanged")
            )
            .Build(true);

        if (IsTest)
        {
            Assert.That(src, Is.Not.Null);
            Assert.That(src, Is.EqualTo(classReference));
        }
    }
}