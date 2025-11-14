namespace Atom.SourceGeneration.Tests;

[Parallelizable(ParallelScope.All)]
public class MethodArgumentBuilderTests(ILogger logger) : BenchmarkTests<MethodArgumentBuilderTests>(logger)
{
    private static string? methodArgumentReference;

    public MethodArgumentBuilderTests() : this(ConsoleLogger.Unicode) { }

    private static void Settings()
    {
        if (string.IsNullOrEmpty(methodArgumentReference) && File.Exists("assets/method_argument.reference"))
            methodArgumentReference = File.ReadAllText("assets/method_argument.reference");
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

    [TestCase(TestName = "Тест сборки аргумента метода"), Benchmark]
    public void FieldTest()
    {
        var src = MethodArgumentMember.Create<int>("arg1").Build(release: true);
        src += ", " + MethodArgumentMember.CreateIn<string>("arg2").Build(release: true);
        src += ", " + MethodArgumentMember.CreateOut<char>("arg3").Build(release: true);
        src += ", " + MethodArgumentMember.CreateRef<bool>("arg4").Build(release: true);
        src += ", " + MethodArgumentMember.CreateParams<byte>("arg5").Build(release: true);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(src, Is.Not.Null);
            Assert.That(src, Is.EqualTo(methodArgumentReference));
        }
    }
}