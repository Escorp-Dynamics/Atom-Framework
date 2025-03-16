using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;

namespace Atom.SourceGeneration.Tests;

public class EventBuilderTests(ILogger logger) : BenchmarkTest<EventBuilderTests>(logger)
{
    private static string? eventReference;

    public override bool IsBenchmarkDisabled => true;

    public EventBuilderTests() : this(ConsoleLogger.Unicode) { }

    private static void Settings()
    {
        if (string.IsNullOrEmpty(eventReference) && File.Exists("assets/event.reference"))
            eventReference = File.ReadAllText("assets/event.reference");
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

    [TestCase(TestName = "Тест сборки события"), Benchmark]
    public void EventTest()
    {
        var src = EventMember.Create()
            .WithComment("Тестовое событие")
            .WithAttribute("Test")
            .WithAccessModifier(AccessModifier.Internal)
            .AsNew()
            .AsVirtual()
            .WithName("TestEvent")
            .WithType<int>()
            .WithAdder()
            .Build(true);

        src += Environment.NewLine + EventMember.Create()
            .WithComment("Тестовое событие")
            .WithAccessModifier(AccessModifier.Internal)
            .WithName("TestEvent")
            .WithType<object>()
            .WithRemover()
            .Build(true);

        src += Environment.NewLine + EventMember.CreateWithAdderOnly<int>("Test2").Build(true);
        src += Environment.NewLine + EventMember.CreateWithRemoverOnly<int>("Test2").Build(true);

        src += Environment.NewLine + EventMember.Create()
            .WithComment("Тестовое событие")
            .WithAccessModifier(AccessModifier.Public)
            .WithName("TestEvent")
            .WithType<object>()
            .WithAdder("SomeEvent += value")
            .Build(true);

        src += Environment.NewLine + EventMember.Create()
            .WithComment("Тестовое событие")
            .WithAccessModifier(AccessModifier.ProtectedInternal)
            .WithName("TestEvent")
            .WithType<object>()
            .WithAdder("SomeEvent += value")
            .WithRemover("SomeEvent -= value")
            .Build(true);

        src += Environment.NewLine + EventMember.Create()
            .WithComment("Тестовое событие")
            .WithAccessModifier(AccessModifier.Public)
            .WithName("TestEvent")
            .WithType<object>()
            .WithAdder(@"
                var value = SomeMethod();
                SomeEvent += value;
            ")
            .Build(true);

        src += Environment.NewLine + EventMember.Create()
            .WithComment("Тестовое событие")
            .WithAccessModifier(AccessModifier.ProtectedInternal)
            .WithName("TestEvent")
            .WithType<object>()
            .WithAdder("SomeEvent += value")
            .WithRemover(@"
                SomeMethod(value);
                SomeEvent -= value;
            ")
            .Build(true);

        src += Environment.NewLine + EventMember.Create<Type?>("SimpleEvent").Build(true);

        if (IsTest)
        {
            Assert.That(src, Is.Not.Null);
            Assert.That(src, Is.EqualTo(eventReference));
        }
    }
}