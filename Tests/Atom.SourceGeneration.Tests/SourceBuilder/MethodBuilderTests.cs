using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.SourceGeneration.Tests;

public class MethodBuilderTests(ILogger logger) : BenchmarkTest<MethodBuilderTests>(logger)
{
    private static string? methodReference;

    public override bool IsBenchmarkDisabled => true;

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
            .AddArgument<bool>("arg1", "Булев аргумент")
            .AddArgument<string>("arg2", "Строковый аргумент", "NotNull")
            .WithComment("Тестовый метод")
            .AsAbstract()
            .Build(true);

        src += Environment.NewLine + MethodMember.Create("TestMethod", AccessModifier.ProtectedInternal)
            .AddArgument<bool>("arg1", "Булев аргумент")
            .AddArgument<string>("arg2", "Строковый аргумент", "NotNull", "Test")
            .WithComment("Тестовый метод")
            .AsVirtual()
            .Build(true);

        src += Environment.NewLine + MethodMember.Create("TestMethod", AccessModifier.ProtectedInternal)
            .AddArgument<bool>("arg1", "Булев аргумент")
            .AddArgument<string>("arg2", "Строковый аргумент", "NotNull", "Test")
            .WithComment("Тестовый метод")
            .AsVirtual()
            .Build(true);

        src += Environment.NewLine + MethodMember.Create<bool>("TestMethod")
            .AddArgument<bool>("arg1", "Булев аргумент")
            .AddArgument<string>("arg2", "Строковый аргумент")
            .WithComment("Тестовый метод")
            .WithCode("arg1 && !string.IsNullOrEmpty(arg2)")
            .Build(true);

        src += Environment.NewLine + MethodMember.Create("TestMethod", true)
            .AddArgument<bool>("arg1", "Булев аргумент")
            .AddArgument<string>("arg2", "Строковый аргумент")
            .WithComment("Тестовый метод")
            .WithCode(@"
                var test = arg1 && !string.IsNullOrEmpty(arg2);
                SomeMethod(test);
            ")
            .Build(true);
        
        src += Environment.NewLine + MethodMember.Create("TestMethod", true)
            .AddArgument<bool>("arg1", "Булев аргумент")
            .AddArgument<string>("arg2", "Строковый аргумент")
            .WithComment("Тестовый метод")
            .AddGeneric("T", "Тестовый тип", "struct")
            .WithCode(@"
                var test = arg1 && !string.IsNullOrEmpty(arg2);
                SomeMethod(test);
            ")
            .Build(true);

        if (IsTest)
        {
            Assert.That(src, Is.Not.Null);
            Assert.That(src, Is.EqualTo(methodReference));
        }
    }
}