namespace Atom.SourceGeneration.Tests;

public class InterfaceBuilderTests(ILogger logger) : BenchmarkTests<InterfaceBuilderTests>(logger)
{
    private static string? interfaceReference;

    public InterfaceBuilderTests() : this(ConsoleLogger.Unicode) { }

    private static void SetUp()
    {
        if (string.IsNullOrEmpty(interfaceReference) && File.Exists("assets/interface.reference"))
            interfaceReference = File.ReadAllText("assets/interface.reference");
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

    [TestCase(TestName = "Тест сборки интерфейса"), Benchmark]
    public void InterfaceTest()
    {
        var src = InterfaceEntity.Create("ITest", AccessModifier.Public)
            .WithComment("Тестовый интерфейс")
            .WithAttribute("Test")
            .WithProperty<int>("TestProperty1")
            .WithEvent<AsyncEventHandler<FailedEventArgs>>("TestEvent")
            .WithMethod("TestMethod")
            .Build(true);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(src, Is.Not.Null);
            Assert.That(src, Is.EqualTo(interfaceReference));
        }
    }
}