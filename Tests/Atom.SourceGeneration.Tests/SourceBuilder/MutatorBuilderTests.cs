using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.SourceGeneration.Tests;

public class MutatorBuilderTests(ILogger logger) : BenchmarkTest<MutatorBuilderTests>(logger)
{
    private static string? propertyMutatorReference;
    private static string? eventRemoveReference;

    public override bool IsBenchmarkDisabled => true;

    public MutatorBuilderTests() : this(ConsoleLogger.Unicode) { }

    private static void Settings()
    {
        if (string.IsNullOrEmpty(propertyMutatorReference) && File.Exists("assets/property_mutator.reference"))
            propertyMutatorReference = File.ReadAllText("assets/property_mutator.reference");

        if (string.IsNullOrEmpty(eventRemoveReference) && File.Exists("assets/event_remove.reference"))
            eventRemoveReference = File.ReadAllText("assets/event_remove.reference");
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

    [TestCase(TestName = "Тест сборки мутатора свойства"), Benchmark]
    public void PropertyMutatorTest()
    {
        var src = PropertyMutatorMember.Create()
            .WithAttribute("Test")
            .WithCode("test = value")
            .Build(true);

        src += Environment.NewLine + PropertyMutatorMember.Create()
            .WithAttribute("Test")
            .WithCode(@"
                SomeMethod(value);
                test = value;
            ")
            .Build(true);

        src += Environment.NewLine + PropertyMutatorMember.Create()
            .WithAttribute("Test")
            .Build(true);

        if (IsTest)
        {
            Assert.That(src, Is.Not.Null);
            Assert.That(src, Is.EqualTo(propertyMutatorReference));
        }
    }

    [TestCase(TestName = "Тест сборки мутатора события"), Benchmark]
    public void EventRemoveTest()
    {
        var src = EventRemoveMember.Create()
            .WithAttribute("Test")
            .WithCode("Test -= OnTest")
            .Build(true);

        src += Environment.NewLine + EventRemoveMember.Create()
            .WithAttribute("Test")
            .WithCode(@"
                SomeMethod();
                Test -= OnTest;
            ")
            .Build(true);

        src += Environment.NewLine + EventRemoveMember.Create()
            .WithAttribute("Test")
            .Build(true);

        if (IsTest)
        {
            Assert.That(src, Is.Not.Null);
            Assert.That(src, Is.EqualTo(eventRemoveReference));
        }
    }
}