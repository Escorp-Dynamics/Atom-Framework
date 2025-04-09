using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using Serilog;

using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Atom.Debug.Logging.Tests;

public class LoggerTests(BenchmarkDotNet.Loggers.ILogger logger) : BenchmarkTests<LoggerTests>(logger)
{
    private static StringWriter? consoleOutput;
    private static TextWriter? originalConsoleOutput;

    private static ILogger? consoleLogger;
    private static ILogger? fileLogger;
    private static ILogger? combinedLogger;

    private static ILoggerFactory? microsoftFactory;
    private static ILogger? microsoftConsoleLogger;

    private static ILoggerFactory? nLogFactory;
    private static ILogger? nLogConsoleLogger;

    private static ILoggerFactory? serilogFactory;
    private static ILogger? serilogConsoleLogger;

    public LoggerTests() : this(BenchmarkDotNet.Loggers.ConsoleLogger.Unicode) { }

    public override void OneTimeSetUp()
    {
        SetUp();
        base.OneTimeSetUp();
    }

    public override void GlobalSetUp()
    {
        SetUp();
        base.GlobalSetUp();
    }

    public override void OneTimeTearDown()
    {
        Dispose();
        base.OneTimeTearDown();
    }

    public override void GlobalTearDown()
    {
        Dispose();
        base.GlobalTearDown();
    }

    [TestCase(TestName = "Консольный тест логирования"), Benchmark]
    public async Task ConsoleTest()
    {
        await Test(consoleLogger);

        if (!IsBenchmarkEnabled)
        {
            var sb = consoleOutput!.GetStringBuilder();
            Assert.That(sb.Length, Is.GreaterThan(0));
        }
    }

    [TestCase(TestName = "Файловый тест логирования")]
    public async Task FileTest()
    {
        await Test(fileLogger);
        if (!IsBenchmarkEnabled) Assert.That(File.Exists(((FileLogger)fileLogger!).Path), Is.True);
    }

    [TestCase(TestName = "Полный тест логирования")]
    public async Task CombinedTest()
    {
        await Test(combinedLogger);

        if (!IsBenchmarkEnabled)
        {
            Assert.Multiple(() =>
            {
                var sb = consoleOutput!.GetStringBuilder();
                Assert.That(sb.Length, Is.GreaterThan(0));

                var log = ((CombinedLogger)combinedLogger!).Loggers.OfType<FileLogger>().First();
                Assert.That(File.Exists(log.Path), Is.True);
            });
        }
    }

    [TestCase(TestName = "Консольный тест логирования (.NET)"), Benchmark]
    public async Task ConsoleTestDotnet()
    {
        await Test(microsoftConsoleLogger);

        if (!IsBenchmarkEnabled)
        {
            var sb = consoleOutput!.GetStringBuilder();
            Assert.That(sb.Length, Is.GreaterThan(0));
        }
    }

    [TestCase(TestName = "Консольный тест логирования (NLog)"), Benchmark]
    public async Task ConsoleTestNLog()
    {
        await Test(nLogConsoleLogger);

        if (!IsBenchmarkEnabled)
        {
            var sb = consoleOutput!.GetStringBuilder();
            Assert.That(sb.Length, Is.GreaterThan(0));
        }
    }

    [TestCase(TestName = "Консольный тест логирования (Serilog)"), Benchmark]
    public async Task ConsoleTestSerilog()
    {
        await Test(serilogConsoleLogger);

        if (!IsBenchmarkEnabled)
        {
            var sb = consoleOutput!.GetStringBuilder();
            Assert.That(sb.Length, Is.GreaterThan(0));
        }
    }

    private static async Task Test(ILogger? logger)
    {
        if (logger is null) return;

        logger.TestInformation();

        using var glob = logger.BeginScope("[GLOBAL]");
        logger.TestInformation2();

        await Task.Run(async () =>
        {
            using var local = logger.BeginScope("[LOCAL]");

            using (var scope = logger.BeginScope("ID 1")) logger.TestInformationWithScope();

            using (var scope = logger.BeginScope("ID 2"))
            {
                logger.StartingTransactionProcessing();

                await Task.Run(() => logger.SubtaskTransactionStep1());

                var thread = new Thread(() => logger.SubtaskTransactionStep2());
                thread.Start();
                thread.Join();

                await Task.Run(() => logger.SubtaskTransactionStep3());

                logger.TransactionProcessingCompleted();
            }

            var tasks = new Task[2];

            tasks[0] = Task.Run(() =>
            {
                using var scope = logger.BeginScope("ID 3");

                for (var i = 0; i < 10; ++i)
                {
                    using var subScope = logger.BeginScope("SubScope " + i);
                    logger.AsyncTest1(i);
                }
            });

            tasks[1] = Task.Run(() =>
            {
                using var scope = logger.BeginScope("ID 4");

                for (var i = 0; i < 20; ++i) logger.AsyncTest2(i);
            });

            var thread2 = new Thread(() =>
            {
                using var scope = logger.BeginScope("ID 5");
                for (var i = 0; i < 15; ++i) logger.ThreadTest1(i);
            });

            thread2.Start();

            var thread3 = new Thread(() =>
            {
                using var scope = logger.BeginScope("ID 6");

                for (var i = 0; i < 15; ++i)
                {
                    using var subScope = logger.BeginScope("SubScope" + i);
                    logger.ThreadTest2(i);
                }
            });

            thread3.Start();

            await Task.WhenAll(tasks);
            thread2.Join();
            thread3.Join();

            using (var scope = logger.BeginScope("ID 7"))
            {
                await foreach (var i in TestEnumerableAsync()) logger.IAsyncEnumerableTest1(i);
            }

            using (var scope = logger.BeginScope("ID 8"))
            {
                await foreach (var i in TestEnumerableAsync())
                {
                    using var subScope = logger.BeginScope("SubScope " + i);
                    logger.IAsyncEnumerableTest1(i);
                }
            }
        });
    }

    private static async IAsyncEnumerable<int> TestEnumerableAsync()
    {
        for (var i = 0; i < 10; ++i) yield return i;
        await Task.Yield();
    }

    private static void SetUp()
    {
        originalConsoleOutput = Console.Out;
        consoleOutput = new StringWriter();
        Console.SetOut(consoleOutput);

        consoleLogger = Logging.Logger.Factory.CreateLogger("TestConsole");

        var provider = new FileLoggerProvider("logs/");
        fileLogger = provider.CreateLogger("TestFile");

        Logging.Logger.Factory.AddProvider(provider);
        combinedLogger = Logging.Logger.Factory.CreateLogger("TestCombined");

        microsoftFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddConsole());
        microsoftConsoleLogger = microsoftFactory.CreateLogger("MicrosoftTestConsole");

        var nLogConfig = new NLog.Config.LoggingConfiguration();

        var consoleTarget = new NLog.Targets.ConsoleTarget("console");
        nLogConfig.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, consoleTarget);

        NLog.LogManager.Configuration = nLogConfig;

        nLogFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddNLog());
        nLogConsoleLogger = nLogFactory.CreateLogger("NLogTestConsole");

        serilogFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddSerilog(new LoggerConfiguration().WriteTo.Console().CreateLogger()));
        serilogConsoleLogger = serilogFactory.CreateLogger("SerilogTestConsole");
    }

    private static void Dispose()
    {
        Console.SetOut(originalConsoleOutput!);
        consoleOutput!.Dispose();
        microsoftFactory!.Dispose();
        nLogFactory!.Dispose();
        serilogFactory!.Dispose();
    }
}