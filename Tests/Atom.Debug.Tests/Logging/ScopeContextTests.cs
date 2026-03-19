using Microsoft.Extensions.Logging;

#pragma warning disable CA2254 // Для тестов можно использовать шаблоны напрямую
#pragma warning disable CA1848 // Для тестов используем методы LoggerExtensions

namespace Atom.Debug.Logging.Tests;

/// <summary>
/// Тесты для <see cref="ScopeContext"/>.
/// </summary>
[Parallelizable(ParallelScope.Self)]
public class ScopeContextTests(BenchmarkDotNet.Loggers.ILogger logger) : BenchmarkTests<ScopeContextTests>(logger)
{
    private StringWriter? consoleOutput;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ScopeContextTests"/>.
    /// </summary>
    public ScopeContextTests() : this(BenchmarkDotNet.Loggers.ConsoleLogger.Unicode) { }

    /// <summary>
    /// Подготовка к тестам.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        consoleOutput = new StringWriter();
    }

    /// <summary>
    /// Очистка после тестов.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        consoleOutput?.Dispose();
    }

    /// <summary>
    /// Тест создания области видимости.
    /// </summary>
    [TestCase(TestName = "Тест BeginScope")]
    public async Task BeginScopeTest()
    {
        using var logger = new ConsoleLogger("Test")
        { Writer = consoleOutput! }
            .WithoutTime()
            .WithoutDate()
            .WithoutStyling();

        using (logger.BeginScope("[Scope1]"))
        {
            logger.Log(LogLevel.Information, "Message in scope");
        }

        await Task.Delay(100);

        var output = consoleOutput!.ToString();
        Assert.That(output, Does.Contain("[Scope1]"));
    }

    /// <summary>
    /// Тест вложенных областей видимости.
    /// </summary>
    [TestCase(TestName = "Тест вложенных BeginScope")]
    public async Task NestedScopeTest()
    {
        using var logger = new ConsoleLogger("Test")
        { Writer = consoleOutput! }
            .WithoutTime()
            .WithoutDate()
            .WithoutStyling();

        using (logger.BeginScope("[Parent]"))
        {
            using (logger.BeginScope("[Child]"))
            {
                logger.Log(LogLevel.Information, "Nested message");
            }
        }

        await Task.Delay(100);

        var output = consoleOutput!.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(output, Does.Contain("[Parent]"));
            Assert.That(output, Does.Contain("[Child]"));
        });
    }

    /// <summary>
    /// Тест ToString для ScopeContext.
    /// </summary>
    [TestCase(TestName = "Тест ScopeContext.ToString")]
    public void ScopeContextToStringTest()
    {
        var parent = new ScopeContext("Parent");
        var child = new ScopeContext("Child") { Parent = parent };

        var result = child.ToString();

        Assert.That(result, Does.Contain("Parent"));
        Assert.That(result, Does.Contain("Child"));
    }

    /// <summary>
    /// Тест события Disposed.
    /// </summary>
    [TestCase(TestName = "Тест ScopeContext.Disposed")]
    public void ScopeContextDisposedEventTest()
    {
        var scope = new ScopeContext("Test");
        var disposed = false;

        scope.Disposed += (sender, args) =>
        {
            disposed = true;
            Assert.That(args.Scope, Is.EqualTo(scope));
        };

        scope.Dispose();

        Assert.That(disposed, Is.True);
    }
}
