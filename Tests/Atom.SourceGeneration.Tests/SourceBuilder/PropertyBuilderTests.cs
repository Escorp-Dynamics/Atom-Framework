namespace Atom.SourceGeneration.Tests;

[Parallelizable(ParallelScope.All)]
public class PropertyBuilderTests(ILogger logger) : BenchmarkTests<PropertyBuilderTests>(logger)
{
    private static string? propertyReference;

    public PropertyBuilderTests() : this(ConsoleLogger.Unicode) { }

    private static void Settings()
    {
        if (string.IsNullOrEmpty(propertyReference) && File.Exists("assets/property.reference"))
            propertyReference = File.ReadAllText("assets/property.reference");
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

    [TestCase(TestName = "Тест сборки свойства"), Benchmark]
    public void PropertyTest()
    {
        var src = PropertyMember.Create()
            .WithComment("Тестовое свойство")
            .WithAttribute("Test")
            .WithAccessModifier(AccessModifier.Internal)
            .AsNew()
            .AsVirtual()
            .WithName("TestProperty")
            .WithType<int>()
            .WithGetter()
            .Build(release: true);

        src += Environment.NewLine + PropertyMember.Create()
            .WithComment("Тестовое свойство")
            .WithAccessModifier(AccessModifier.Internal)
            .WithName("TestProperty")
            .WithType<object>()
            .WithSetter()
            .Build(release: true);

        src += Environment.NewLine + PropertyMember.CreateWithGetterOnly<string>("Test2").WithInitialValue<string>("str").Build(release: true);
        src += Environment.NewLine + PropertyMember.CreateWithGetterOnly<bool>("Test2").WithInitialValue("anotherVar").Build(release: true);
        src += Environment.NewLine + PropertyMember.CreateWithSetterOnly<int>("Test2", isInit: true).Build(release: true);

        src += Environment.NewLine + PropertyMember.Create()
            .WithComment("Тестовое свойство")
            .WithAccessModifier(AccessModifier.Public)
            .WithName("TestProperty")
            .WithType<object>()
            .WithGetter("field")
            .Build(release: true);

        src += Environment.NewLine + PropertyMember.Create()
            .WithComment("Тестовое свойство")
            .WithAccessModifier(AccessModifier.ProtectedInternal)
            .WithName("TestProperty")
            .WithType<object>()
            .WithGetter("field")
            .WithSetter("field = value")
            .Build(release: true);

        src += Environment.NewLine + PropertyMember.Create()
            .WithComment("Тестовое свойство")
            .WithAccessModifier(AccessModifier.Public)
            .WithName("TestProperty")
            .WithType<object>()
            .WithGetter(@"
                var value = SomeMethod();
                return value;
            ")
            .Build(release: true);

        src += Environment.NewLine + PropertyMember.Create()
            .WithComment("Тестовое свойство")
            .WithAccessModifier(AccessModifier.ProtectedInternal)
            .WithName("TestProperty")
            .WithType<object>()
            .WithGetter("field")
            .WithSetter(@"
                SomeMethod(value);
                field = value;
            ")
            .Build(release: true);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(src, Is.Not.Null);
            Assert.That(src, Is.EqualTo(propertyReference));
        }
    }
}