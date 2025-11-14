namespace Atom.SourceGeneration.Tests;

[Parallelizable(ParallelScope.All)]
public class SourceBuilderTests(ILogger logger) : BenchmarkTests<SourceBuilderTests>(logger)
{
    private static string? sourceReference;

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
            .Build(release: true);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(src, Is.Not.Null);
            Assert.That(src, Is.EqualTo(sourceReference));
        }
    }
}