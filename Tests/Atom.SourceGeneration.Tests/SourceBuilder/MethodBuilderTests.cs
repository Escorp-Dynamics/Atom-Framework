namespace Atom.SourceGeneration.Tests;

[Parallelizable(ParallelScope.All)]
public class MethodBuilderTests(ILogger logger) : BenchmarkTests<MethodBuilderTests>(logger)
{
    private static string? methodReference;

    public MethodBuilderTests() : this(ConsoleLogger.Unicode) { }

    private static void SetUp()
    {
        if (string.IsNullOrEmpty(methodReference) && File.Exists("assets/method.reference"))
            methodReference = File.ReadAllText("assets/method.reference");
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

    [TestCase(TestName = "Тест сборки однострочного метода"), Benchmark]
    public void MethodTest()
    {
        var src = MethodMember.Create<int>("TestMethod", AccessModifier.Protected)
            .WithArgument<bool>("arg1")
            .WithArgument<string>("arg2", "NotNull")
            .WithComment("Тестовый метод")
            .AsAbstract()
            .Build(release: true);

        src += Environment.NewLine + MethodMember.Create("TestMethod", AccessModifier.ProtectedInternal)
            .WithArgument<bool>("arg1")
            .WithArgument<string>("arg2", "NotNull", "Test")
            .WithComment("Тестовый метод")
            .AsVirtual()
            .Build(release: true);

        src += Environment.NewLine + MethodMember.Create("TestMethod", AccessModifier.ProtectedInternal)
            .WithArgument<bool>("arg1")
            .WithArgument<string>("arg2", "NotNull", "Test")
            .WithComment("Тестовый метод")
            .AsVirtual()
            .Build(release: true);

        src += Environment.NewLine + MethodMember.Create<bool>("TestMethod")
            .WithArgument<bool>("arg1")
            .WithArgument<string>("arg2")
            .WithComment("Тестовый метод")
            .WithCode("arg1 && !string.IsNullOrEmpty(arg2)")
            .Build(release: true);

        src += Environment.NewLine + MethodMember.Create("TestMethod", isStatic: true)
            .WithArgument<bool>("arg1")
            .WithArgument<string>("arg2")
            .WithComment("Тестовый метод")
            .WithCode(@"
                var test = arg1 && !string.IsNullOrEmpty(arg2);
                SomeMethod(test);
            ")
            .Build(release: true);

        src += Environment.NewLine + MethodMember.Create("TestMethod", isStatic: true)
            .WithArgument<bool>("arg1")
            .WithArgument<string>("arg2")
            .WithComment("Тестовый метод")
            .WithGeneric("T", "struct")
            .WithCode(@"
                var test = arg1 && !string.IsNullOrEmpty(arg2);
                SomeMethod(test);
            ")
            .Build(release: true);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(src, Is.Not.Null);
            Assert.That(src, Is.EqualTo(methodReference));
        }
    }
}