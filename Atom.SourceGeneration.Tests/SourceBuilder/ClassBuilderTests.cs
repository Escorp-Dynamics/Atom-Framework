using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.SourceGeneration.Tests;

public class ClassBuilderTests(ILogger logger) : BenchmarkTest<ClassBuilderTests>(logger)
{
    private static string? classReference;

    public override bool IsBenchmarkDisabled => true;

    public ClassBuilderTests() : this(ConsoleLogger.Unicode) { }

    private static void SetUp()
    {
        if (string.IsNullOrEmpty(classReference) && File.Exists("assets/class.reference"))
            classReference = File.ReadAllText("assets/class.reference");
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

    [TestCase(TestName = "Тест сборки класса"), Benchmark]
    public void ClassTest()
    {
        var src = ClassEntity.Create("Test", AccessModifier.Public)
            .WithComment("Тестовый класс")
            .WithAttribute("Test")
            .AsPartial()
            .AddField<object>("field")
            .AddProperty<object>("TestProperty1")
            .AddEvent<Action>("TestEvent")
            .AddMethod("TestMethod")
            .Build(true);

        if (IsTest)
        {
            Assert.That(src, Is.Not.Null);
            Assert.That(src, Is.EqualTo(classReference));
        }
    }
}