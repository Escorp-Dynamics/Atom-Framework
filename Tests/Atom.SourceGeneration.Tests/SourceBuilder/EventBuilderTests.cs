namespace Atom.SourceGeneration.Tests;

[Parallelizable(ParallelScope.All)]
public class EventBuilderTests(ILogger logger) : BenchmarkTests<EventBuilderTests>(logger)
{
    private static string? eventReference;

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
            .Build(release: true);

        src += Environment.NewLine + EventMember.Create()
            .WithComment("Тестовое событие")
            .WithAccessModifier(AccessModifier.Internal)
            .WithName("TestEvent")
            .WithType<object>()
            .WithRemover()
            .Build(release: true);

        src += Environment.NewLine + EventMember.CreateWithAdderOnly<int>("Test2").Build(release: true);
        src += Environment.NewLine + EventMember.CreateWithRemoverOnly<int>("Test2").Build(release: true);

        src += Environment.NewLine + EventMember.Create()
            .WithComment("Тестовое событие")
            .WithAccessModifier(AccessModifier.Public)
            .WithName("TestEvent")
            .WithType<object>()
            .WithAdder("SomeEvent += value")
            .Build(release: true);

        src += Environment.NewLine + EventMember.Create()
            .WithComment("Тестовое событие")
            .WithAccessModifier(AccessModifier.ProtectedInternal)
            .WithName("TestEvent")
            .WithType<object>()
            .WithAdder("SomeEvent += value")
            .WithRemover("SomeEvent -= value")
            .Build(release: true);

        src += Environment.NewLine + EventMember.Create()
            .WithComment("Тестовое событие")
            .WithAccessModifier(AccessModifier.Public)
            .WithName("TestEvent")
            .WithType<object>()
            .WithAdder(@"
                var value = SomeMethod();
                SomeEvent += value;
            ")
            .Build(release: true);

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
            .Build(release: true);

        src += Environment.NewLine + EventMember.Create<Type?>("SimpleEvent").Build(release: true);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(src, Is.Not.Null);
            Assert.That(src, Is.EqualTo(eventReference));
        }
    }
}