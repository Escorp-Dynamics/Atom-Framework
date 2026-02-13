using Microsoft.Extensions.Logging;

#pragma warning disable CA2254 // Для тестов можно использовать шаблоны напрямую
#pragma warning disable CA1848 // Для тестов используем методы LoggerExtensions

namespace Atom.Debug.Logging.Tests;

/// <summary>
/// Тесты для <see cref="ConsoleLogger"/>.
/// </summary>
[Parallelizable(ParallelScope.Self)]
public class ConsoleLoggerTests(BenchmarkDotNet.Loggers.ILogger logger) : BenchmarkTests<ConsoleLoggerTests>(logger)
{
    private StringWriter? consoleOutput;
    private TextWriter? originalConsoleOutput;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ConsoleLoggerTests"/>.
    /// </summary>
    public ConsoleLoggerTests() : this(BenchmarkDotNet.Loggers.ConsoleLogger.Unicode) { }

    /// <summary>
    /// Подготовка к тестам.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        originalConsoleOutput = Console.Out;
        consoleOutput = new StringWriter();
        Console.SetOut(consoleOutput);
    }

    /// <summary>
    /// Очистка после тестов.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        Console.SetOut(originalConsoleOutput!);
        consoleOutput?.Dispose();
    }

    /// <summary>
    /// Тест настройки формата даты.
    /// </summary>
    [TestCase(TestName = "Тест WithDate с форматом")]
    public async Task WithDateFormatTest()
    {
        using var logger = new ConsoleLogger("Test")
            .WithDate("yyyy-MM-dd")
            .WithoutTime()
            .WithoutStyling();

        logger.Log(LogLevel.Information, "Test message");

        await Task.Delay(100);

        var output = consoleOutput!.ToString();
        Assert.That(output, Does.Match(@"\d{4}-\d{2}-\d{2}"));
    }

    /// <summary>
    /// Тест настройки формата времени.
    /// </summary>
    [TestCase(TestName = "Тест WithTime с форматом")]
    public async Task WithTimeFormatTest()
    {
        using var logger = new ConsoleLogger("Test")
            .WithTime("HH:mm")
            .WithoutDate()
            .WithoutStyling();

        logger.Log(LogLevel.Information, "Test message");

        await Task.Delay(100);

        var output = consoleOutput!.ToString();
        Assert.That(output, Does.Match(@"\d{2}:\d{2}"));
    }

    /// <summary>
    /// Тест вывода названия категории.
    /// </summary>
    [TestCase(TestName = "Тест WithCategoryName")]
    public async Task WithCategoryNameTest()
    {
        using var logger = new ConsoleLogger("MyCategory")
            .WithCategoryName()
            .WithoutTime()
            .WithoutDate()
            .WithoutStyling();

        logger.Log(LogLevel.Information, "Test message");

        await Task.Delay(100);

        var output = consoleOutput!.ToString();
        Assert.That(output, Does.Contain("MyCategory"));
    }

    /// <summary>
    /// Тест отключения уровней логирования.
    /// </summary>
    [TestCase(TestName = "Тест WithoutLogLevels")]
    public async Task WithoutLogLevelsTest()
    {
        using var logger = new ConsoleLogger("Test")
            .WithoutLogLevels(LogLevel.Information)
            .WithoutStyling();

        logger.Log(LogLevel.Information, "Should not appear");
        logger.Log(LogLevel.Warning, "Should appear");

        await Task.Delay(100);

        var output = consoleOutput!.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(output, Does.Not.Contain("Should not appear"));
            Assert.That(output, Does.Contain("Should appear"));
        });
    }

    /// <summary>
    /// Тест включения конкретных уровней логирования.
    /// </summary>
    [TestCase(TestName = "Тест WithLogLevels")]
    public async Task WithLogLevelsTest()
    {
        using var logger = new ConsoleLogger("Test")
            .WithoutLogLevels(LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Critical)
            .WithLogLevels(LogLevel.Error)
            .WithoutStyling();

        logger.Log(LogLevel.Information, "Info - should not appear");
        logger.Log(LogLevel.Error, "Error - should appear");

        await Task.Delay(100);

        var output = consoleOutput!.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(output, Does.Not.Contain("Info - should not appear"));
            Assert.That(output, Does.Contain("Error - should appear"));
        });
    }

    /// <summary>
    /// Тест проверки IsEnabled.
    /// </summary>
    [TestCase(TestName = "Тест IsEnabled")]
    public void IsEnabledTest()
    {
        using var logger = new ConsoleLogger("Test")
            .WithoutLogLevels(LogLevel.Debug)
            .WithLogLevels(LogLevel.Information);

        Assert.Multiple(() =>
        {
            Assert.That(logger.IsEnabled(LogLevel.Information), Is.True);
            Assert.That(logger.IsEnabled(LogLevel.Debug), Is.False);
        });
    }

    /// <summary>
    /// Тест Generic-версии логгера.
    /// </summary>
    [TestCase(TestName = "Тест ConsoleLogger<T>")]
    public async Task GenericLoggerTest()
    {
        using var logger = new ConsoleLogger<ConsoleLoggerTests>()
            .WithCategoryName()
            .WithoutTime()
            .WithoutDate()
            .WithoutStyling();

        logger.Log(LogLevel.Information, "Generic test");

        await Task.Delay(200);

        var output = consoleOutput!.ToString();
        Assert.That(output, Does.Contain(nameof(ConsoleLoggerTests)));
    }
}
