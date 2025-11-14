namespace Tests;

[TestFixture, Parallelizable(ParallelScope.All)]
public class WebDriverTests(ILogger logger) : BenchmarkTests<WebDriverTests>(logger)
{
    public override bool IsBenchmarkEnabled => default;

    public WebDriverTests() : this(ConsoleLogger.Unicode) { }
}