using System.Diagnostics;

namespace Atom.Threading.Tests;

public class RateLimiterTests(ILogger logger) : BenchmarkTests<RateLimiterTests>(logger)
{
    public RateLimiterTests() : this(ConsoleLogger.Unicode) { }

    private void Log(string? message)
    {
        message = $"{DateTime.UtcNow:HH:mm:ss.fff} {message}";
        Logger.WriteLineInfo(message);
        Trace.TraceInformation(message);
    }

    private async ValueTask TestCallbackAsync()
    {
        Log("TestCallbackAsync(): START");
        await Task.Delay(TimeSpan.FromMilliseconds(1500));
        Log("TestCallbackAsync(): END");
    }

    [TestCase(TestName = "Тест проверки пропускной способности"), Benchmark]
    public async Task ManualTestAsync()
    {
        using var limiter = new RateLimiter(1, 1000);

        for (var i = 0; i < 10; ++i) await limiter.CallAsync(TestCallbackAsync);

        if (!IsBenchmarkEnabled) Assert.Pass();
    }
}