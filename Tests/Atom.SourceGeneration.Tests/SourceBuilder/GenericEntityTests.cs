namespace Atom.SourceGeneration.Tests;

/// <summary>
/// Тесты для <see cref="GenericEntity"/>.
/// </summary>
[Parallelizable(ParallelScope.All)]
public class GenericEntityTests(ILogger logger) : BenchmarkTests<GenericEntityTests>(logger)
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="GenericEntityTests"/>.
    /// </summary>
    public GenericEntityTests() : this(ConsoleLogger.Unicode) { }

    /// <summary>
    /// Тест создания простого шаблонного типа.
    /// </summary>
    [TestCase(TestName = "Тест сборки простого шаблонного типа"), Benchmark]
    public void SimpleGenericTest()
    {
        var generic = GenericEntity.Create("T");
        var result = generic.Build(release: true);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.EqualTo("T"));
        }
    }

    /// <summary>
    /// Тест создания шаблонного типа с ограничениями.
    /// </summary>
    [TestCase(TestName = "Тест сборки шаблонного типа с ограничениями"), Benchmark]
    public void GenericWithLimitationsTest()
    {
        var generic = GenericEntity.Create("T", "class", "IDisposable", "new()");
        var limitations = generic.BuildLimitations(release: false);
        var result = generic.Build(release: true);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(result, Is.EqualTo("T"));
            Assert.That(limitations, Is.EqualTo("where T : class, IDisposable, new()"));
        }
    }

    /// <summary>
    /// Тест создания шаблонного типа с атрибутами.
    /// </summary>
    [TestCase(TestName = "Тест сборки шаблонного типа с атрибутами"), Benchmark]
    public void GenericWithAttributesTest()
    {
        var generic = GenericEntity.Create()
            .WithName("T")
            .WithAttribute("DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)")
            .WithLimitation("struct");

        var result = generic.Build(release: false);
        var limitations = generic.BuildLimitations(release: true);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(result, Is.EqualTo("[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T"));
            Assert.That(limitations, Is.EqualTo("where T : struct"));
        }
    }

    /// <summary>
    /// Тест флагов инвариантности и ковариантности.
    /// </summary>
    [TestCase(TestName = "Тест флагов In/Out для шаблонного типа"), Benchmark]
    public void GenericInOutFlagsTest()
    {
        var inGeneric = GenericEntity.Create("TIn").AsIn();
        var outGeneric = GenericEntity.Create("TOut").AsOut();

        if (!IsBenchmarkEnabled)
        {
            Assert.That(inGeneric.IsIn, Is.True);
            Assert.That(inGeneric.IsOut, Is.False);
            Assert.That(outGeneric.IsIn, Is.False);
            Assert.That(outGeneric.IsOut, Is.True);
        }

        inGeneric.Release();
        outGeneric.Release();
    }

    /// <summary>
    /// Тест валидации шаблонного типа.
    /// </summary>
    [TestCase(TestName = "Тест валидации шаблонного типа"), Benchmark]
    public void GenericValidationTest()
    {
        var validGeneric = GenericEntity.Create("T");
        var invalidGeneric = GenericEntity.Create();

        if (!IsBenchmarkEnabled)
        {
            Assert.That(validGeneric.IsValid, Is.True);
            Assert.That(invalidGeneric.IsValid, Is.False);
            Assert.That(invalidGeneric.Build(), Is.Null);
        }

        validGeneric.Release();
        invalidGeneric.Release();
    }

    /// <summary>
    /// Тест построения ограничений без ограничений.
    /// </summary>
    [TestCase(TestName = "Тест построения ограничений без ограничений"), Benchmark]
    public void GenericNoLimitationsTest()
    {
        var generic = GenericEntity.Create("T");
        var limitations = generic.BuildLimitations(release: true);

        if (!IsBenchmarkEnabled)
        {
            Assert.That(limitations, Is.Null);
        }
    }

    /// <summary>
    /// Тест комментария шаблонного типа.
    /// </summary>
    [TestCase(TestName = "Тест комментария шаблонного типа"), Benchmark]
    public void GenericCommentTest()
    {
        var generic = GenericEntity.Create("T")
            .WithComment("Тип элемента коллекции");

        if (!IsBenchmarkEnabled)
        {
            Assert.That(generic.Comment, Is.EqualTo("Тип элемента коллекции"));
        }

        generic.Release();
    }
}
