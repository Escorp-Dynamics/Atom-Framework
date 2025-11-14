namespace Atom.SourceGeneration.Tests;

[Parallelizable(ParallelScope.All)]
public class EnumBuilderTests(ILogger logger) : BenchmarkTests<EnumBuilderTests>(logger)
{
    private static string? enumReference;

    public EnumBuilderTests() : this(ConsoleLogger.Unicode) { }

    private static void Settings()
    {
        if (string.IsNullOrEmpty(enumReference) && File.Exists("assets/enum.reference"))
            enumReference = File.ReadAllText("assets/enum.reference");
    }

    public override void GlobalSetUp()
    {
        Settings();
        base.GlobalSetUp();
    }

    public override void OneTimeSetUp()
    {
        Settings();
        base.OneTimeSetUp();
    }

    [TestCase(TestName = "Тест сборки перечисления"), Benchmark]
    public void EnumTest()
    {
        var src = EnumEntity.Create()
            .WithComment("Тестовое перечисление")
            .WithAttribute("Test")
            .WithAccessModifier(AccessModifier.Internal)
            .WithName("TestEnum")
            .WithType<short>()
            .WithValue("Value1", 0x00, "Значение 1")
            .WithValue("Value2", 0x01, "Значение 2")
            .WithValue("Value3", 0x02, "Значение 3")
            .AsFlags()
            .Build(release: true);

        src += Environment.NewLine + EnumEntity.Create()
            .WithComment("Тестовое перечисление")
            .WithAttribute("Test")
            .WithAccessModifier(AccessModifier.Internal)
            .WithName("TestEnum")
            .WithValue("Value1", "Значение 1")
            .WithValue("Value2", "Значение 2")
            .WithValue("Value3", "Значение 3")
            .Build(release: true);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(src, Is.Not.Null);
            Assert.That(src, Is.EqualTo(enumReference));
        }
    }
}