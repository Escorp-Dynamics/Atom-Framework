using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.SourceGeneration.Tests;

public class FieldBuilderTests(ILogger logger) : BenchmarkTest<FieldBuilderTests>(logger)
{
    private static string? fieldReference;
    private static string? constReference;

    public override bool IsBenchmarkDisabled => true;

    public FieldBuilderTests() : this(ConsoleLogger.Unicode) { }

    private static void Settings()
    {
        if (string.IsNullOrEmpty(fieldReference) && File.Exists("assets/field.reference"))
            fieldReference = File.ReadAllText("assets/field.reference");

        if (string.IsNullOrEmpty(constReference) && File.Exists("assets/const.reference"))
            constReference = File.ReadAllText("assets/const.reference");
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

    [TestCase(TestName = "Тест сборки поля"), Benchmark]
    public void FieldTest()
    {
        var src = FieldMember.Create()
            .WithComment("Тестовое поле")
            .WithAttribute("Test")
            .WithAccessModifier(AccessModifier.Private)
            .WithName("testField")
            .AsStatic()
            .AsReadOnly()
            .AsVolatile()
            .AsRef()
            .WithType<int>()
            .WithValue(5)
            .Build(true);

        if (IsTest)
        {
            Assert.That(src, Is.Not.Null);
            Assert.That(src, Is.EqualTo(fieldReference));
        }
    }

    [TestCase(TestName = "Тест сборки константы"), Benchmark]
    public void ConstTest()
    {
        var src = FieldMember.Create()
            .WithComment("Тестовая константа")
            .WithAccessModifier(AccessModifier.Private)
            .WithName("TestConst")
            .AsConstant()
            .WithType<int>()
            .WithValue(5)
            .Build(true);

        if (IsTest)
        {
            Assert.That(src, Is.Not.Null);
            Assert.That(src, Is.EqualTo(constReference));
        }
    }
}