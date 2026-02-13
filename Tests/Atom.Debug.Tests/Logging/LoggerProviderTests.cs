namespace Atom.Debug.Logging.Tests;

/// <summary>
/// Тесты для провайдеров логгеров.
/// </summary>
[Parallelizable(ParallelScope.Self)]
public class LoggerProviderTests(ILogger logger) : BenchmarkTests<LoggerProviderTests>(logger)
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="LoggerProviderTests"/>.
    /// </summary>
    public LoggerProviderTests() : this(BenchmarkDotNet.Loggers.ConsoleLogger.Unicode) { }

    /// <summary>
    /// Тест создания консольного логгера через провайдер.
    /// </summary>
    [TestCase(TestName = "Тест ConsoleLoggerProvider.CreateLogger")]
    public void ConsoleLoggerProviderCreateLoggerTest()
    {
        using var provider = new ConsoleLoggerProvider();

        var logger1 = provider.CreateLogger("Category1");
        var logger2 = provider.CreateLogger("Category1");
        var logger3 = provider.CreateLogger("Category2");

        Assert.Multiple(() =>
        {
            Assert.That(logger1, Is.Not.Null);
            Assert.That(logger1, Is.InstanceOf<ConsoleLogger>());
            Assert.That(logger1, Is.SameAs(logger2)); // Кэширование
            Assert.That(logger1, Is.Not.SameAs(logger3)); // Разные категории
        });
    }

    /// <summary>
    /// Тест создания файлового логгера через провайдер.
    /// </summary>
    [TestCase(TestName = "Тест FileLoggerProvider.CreateLogger")]
    public void FileLoggerProviderCreateLoggerTest()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "AtomDebugTests", Guid.NewGuid().ToString());

        try
        {
            using var provider = new FileLoggerProvider(testDir + "/");

            var logger = provider.CreateLogger("TestCategory");

            Assert.Multiple(() =>
            {
                Assert.That(logger, Is.Not.Null);
                Assert.That(logger, Is.InstanceOf<FileLogger>());
            });
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, recursive: true);
        }
    }

    /// <summary>
    /// Тест FileLoggerProvider с указанным путём.
    /// </summary>
    [TestCase(TestName = "Тест FileLoggerProvider.CreateLogger с путём")]
    public void FileLoggerProviderCreateLoggerWithPathTest()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "AtomDebugTests", Guid.NewGuid().ToString());
        var testPath = Path.Combine(testDir, "custom.log");

        try
        {
            using var provider = new FileLoggerProvider(testDir + "/");

            var logger = provider.CreateLogger("TestCategory", testPath);

            Assert.Multiple(() =>
            {
                Assert.That(logger, Is.Not.Null);
                Assert.That(logger, Is.InstanceOf<FileLogger>());
                Assert.That(((FileLogger)logger).Path, Is.EqualTo(testPath));
            });
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, recursive: true);
        }
    }

    /// <summary>
    /// Тест LoggerFactory без провайдеров.
    /// </summary>
    [TestCase(TestName = "Тест LoggerFactory без провайдеров")]
    public void LoggerFactoryNoProvidersTest()
    {
        var factory = new LoggerFactory();

        Assert.Throws<InvalidOperationException>(() => factory.CreateLogger("Test"));
    }

    /// <summary>
    /// Тест LoggerFactory с одним провайдером.
    /// </summary>
    [TestCase(TestName = "Тест LoggerFactory с одним провайдером")]
    public void LoggerFactorySingleProviderTest()
    {
        var factory = new LoggerFactory();
        factory.AddProvider(new ConsoleLoggerProvider());

        var logger = factory.CreateLogger("Test");

        Assert.That(logger, Is.InstanceOf<ConsoleLogger>());
    }

    /// <summary>
    /// Тест LoggerFactory с несколькими провайдерами.
    /// </summary>
    [TestCase(TestName = "Тест LoggerFactory с несколькими провайдерами")]
    public void LoggerFactoryMultipleProvidersTest()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "AtomDebugTests", Guid.NewGuid().ToString());

        try
        {
            var factory = new LoggerFactory();
            factory.AddProvider(new ConsoleLoggerProvider());
            factory.AddProvider(new FileLoggerProvider(testDir + "/"));

            var logger = factory.CreateLogger("Test");

            Assert.That(logger, Is.InstanceOf<CombinedLogger>());
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, recursive: true);
        }
    }

    /// <summary>
    /// Тест Dispose для провайдера.
    /// </summary>
    [TestCase(TestName = "Тест ConsoleLoggerProvider.Dispose")]
    public void ProviderDisposeTest()
    {
        var provider = new ConsoleLoggerProvider();
        var logger = provider.CreateLogger("Test") as ConsoleLogger;

        provider.Dispose();

        // После Dispose провайдер не должен выбрасывать исключение при повторном Dispose
        Assert.DoesNotThrow(provider.Dispose);
    }
}
