namespace Atom.SourceGeneration.Tests;

public class AccessorBuilderTests(ILogger logger) : BenchmarkTests<AccessorBuilderTests>(logger)
{
    private static string? propertyAccessorReference;
    private static string? eventAddReference;

    public AccessorBuilderTests() : this(ConsoleLogger.Unicode) { }

    private static void Settings()
    {
        if (string.IsNullOrEmpty(propertyAccessorReference) && File.Exists("assets/property_accessor.reference"))
            propertyAccessorReference = File.ReadAllText("assets/property_accessor.reference");

        if (string.IsNullOrEmpty(eventAddReference) && File.Exists("assets/event_add.reference"))
            eventAddReference = File.ReadAllText("assets/event_add.reference");
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

    [TestCase(TestName = "Тест сборки асессора свойства"), Benchmark]
    public void PropertyAccessorTest()
    {
        var src = PropertyAccessorMember.Create()
            .WithAttribute("Test")
            .AsReadOnly()
            .WithCode("test")
            .Build(true);

        src += Environment.NewLine + PropertyAccessorMember.Create()
            .WithAttribute("Test")
            .AsReadOnly()
            .WithCode(@"
                SomeMethod();
                return test;
            ")
            .Build(true);

        src += Environment.NewLine + PropertyAccessorMember.Create()
            .WithAttribute("Test")
            .Build(true);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(src, Is.Not.Null);
            Assert.That(src, Is.EqualTo(propertyAccessorReference));
        }
    }

    [TestCase(TestName = "Тест сборки асессора события"), Benchmark]
    public void EventAddTest()
    {
        var src = EventAddMember.Create()
            .WithAttribute("Test")
            .WithCode("Test += OnTest")
            .Build(true);

        src += Environment.NewLine + EventAddMember.Create()
            .WithAttribute("Test")
            .WithCode(@"
                SomeMethod();
                Test += OnTest;
            ")
            .Build(true);

        src += Environment.NewLine + EventAddMember.Create()
            .WithAttribute("Test")
            .Build(true);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(src, Is.Not.Null);
            Assert.That(src, Is.EqualTo(eventAddReference));
        }
    }
}