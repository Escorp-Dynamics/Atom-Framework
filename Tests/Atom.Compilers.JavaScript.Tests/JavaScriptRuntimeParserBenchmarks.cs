using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Atom.Compilers.JavaScript.Tests;

[Parallelizable(ParallelScope.All)]
public class JavaScriptRuntimeParserBenchmarks(ILogger logger) : BenchmarkTests<JavaScriptRuntimeParserBenchmarks>(logger)
{
    private const string BenchmarkEnvVar = "ATOM_RUN_BENCHMARKS";
    private const string BenchmarkSummaryFileName = "parser-benchmark-summary.txt";
    private const string BenchmarkBaselineFileName = "parser-benchmark-baseline.txt";
    private const string UpdateBaselineEnvVar = "ATOM_UPDATE_BENCHMARK_BASELINE";

    private JavaScriptRuntimeExecutionState executionState;
    private string shortExpression = null!;
    private string regexHeavyExpression = null!;
    private string templateHeavyExpression = null!;

    public JavaScriptRuntimeParserBenchmarks() : this(ConsoleLogger.Unicode) { }

    public override void OneTimeSetUp()
    {
        IsBenchmarkEnabled = string.Equals(Environment.GetEnvironmentVariable(BenchmarkEnvVar), "1", StringComparison.Ordinal);
        Initialize();
        base.OneTimeSetUp();
    }

    public override void GlobalSetUp()
    {
        Initialize();
        base.GlobalSetUp();
    }

    [TestCase(TestName = "Parser BenchmarkDotNet")]
    public override void RunBenchmarks()
    {
        IsBenchmarkEnabled = string.Equals(Environment.GetEnvironmentVariable(BenchmarkEnvVar), "1", StringComparison.Ordinal);
        TestContext.Out.WriteLine($"Parser benchmark env: {IsBenchmarkEnabled}");
        if (!IsBenchmarkEnabled) return;

        TestContext.Out.WriteLine("Starting parser BenchmarkRunner");
        var config = ManualConfig
            .Create(DefaultConfig.Instance)
            .WithOptions(ConfigOptions.DisableOptimizationsValidator);

        var summary = BenchmarkRunner.Run<JavaScriptRuntimeParserBenchmarkSuite>(config);
        WriteStableSummary(summary);
        Assert.That(summary, Is.Not.Null);
        Assert.That(summary.Reports.Length, Is.GreaterThan(0));
    }

    [TestCase(TestName = "Parser short expression"), Benchmark(Description = "Parser short expression", Baseline = true)]
    public void ParseShortExpression()
        => Parse(shortExpression);

    [TestCase(TestName = "Parser regex-heavy expression"), Benchmark(Description = "Parser regex-heavy expression")]
    public void ParseRegexHeavyExpression()
        => Parse(regexHeavyExpression);

    [TestCase(TestName = "Parser template-heavy expression"), Benchmark(Description = "Parser template-heavy expression")]
    public void ParseTemplateHeavyExpression()
        => Parse(templateHeavyExpression);

    private void Initialize()
    {
        var runtime = new JavaScriptRuntime();
        _ = runtime.Execute(ReadOnlySpan<char>.Empty);
        executionState = runtime.CurrentExecutionState;
        runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();

        shortExpression = "host.connect(left, right ?? fallback) ? left.value : right?.value";
        regexHeavyExpression = "return /[a/b]{2,4}\\d+/g.test(value) ? source[index] : fallback[index + 1]";
        templateHeavyExpression = "`${/}/.test(value) ? host.connect() : `${left ?? right}`}`";
    }

    private void Parse(string source)
    {
        if (!JavaScriptRuntimeParserStageScaffold.TryParse(
                new JavaScriptRuntimeExecutionRequest(
                    executionState,
                    source.AsSpan(),
                    JavaScriptRuntimeExecutionOperationKind.Evaluate),
                out _))
        {
            throw new InvalidOperationException("Parser benchmark input failed to parse.");
        }
    }

    private static void WriteStableSummary(BenchmarkDotNet.Reports.Summary summary)
    {
        var summaryPath = Path.Combine(AppContext.BaseDirectory, BenchmarkSummaryFileName);
        var lines = new List<string>
        {
            $"Title: {summary.Title}",
            $"ReportCount: {summary.Reports.Length}",
        };

        var latestLogPath = TryGetLatestBenchmarkLogPath();

        if (!string.IsNullOrEmpty(latestLogPath))
        {
            lines.Add($"SourceLog: {latestLogPath}");

            foreach (var line in File.ReadLines(latestLogPath))
            {
                if (line.StartsWith("| 'Parser", StringComparison.Ordinal))
                    lines.Add(line);
            }
        }

        if (lines.Count == 2)
        {
            foreach (var report in summary.Reports)
            {
                var statistics = report.ResultStatistics;
                lines.Add($"{report.BenchmarkCase.Descriptor.WorkloadMethod.Name}|Mean={statistics?.Mean}|StdDev={statistics?.StandardDeviation}");
            }
        }

        File.WriteAllLines(summaryPath, lines);

        var snapshotSuffix = ExtractSnapshotSuffix(summary.Title, latestLogPath);

        if (!string.IsNullOrEmpty(snapshotSuffix))
        {
            var snapshotPath = Path.Combine(AppContext.BaseDirectory, $"parser-benchmark-summary-{snapshotSuffix}.txt");
            File.WriteAllLines(snapshotPath, lines);
        }

        WriteBenchmarkBaseline(lines);
    }

    private static void WriteBenchmarkBaseline(List<string> lines)
    {
        var baselinePath = Path.Combine(AppContext.BaseDirectory, BenchmarkBaselineFileName);
        var updateBaseline = string.Equals(Environment.GetEnvironmentVariable(UpdateBaselineEnvVar), "1", StringComparison.Ordinal);

        if (!updateBaseline)
            return;

        File.WriteAllLines(baselinePath, lines);
    }

    private static string? TryGetLatestBenchmarkLogPath()
    {
        var artifactsDirectory = Path.Combine(AppContext.BaseDirectory, "BenchmarkDotNet.Artifacts");

        if (!Directory.Exists(artifactsDirectory))
            return null;

        return Directory
            .GetFiles(artifactsDirectory, "Atom.Compilers.JavaScript.Tests.JavaScriptRuntimeParserBenchmarkSuite-*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(static path => Path.GetFileName(path), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static string? ExtractSnapshotSuffix(string summaryTitle, string? latestLogPath)
    {
        if (!string.IsNullOrEmpty(latestLogPath))
        {
            var fileName = Path.GetFileNameWithoutExtension(latestLogPath);
            var lastDashIndex = fileName.LastIndexOf('-');

            if (lastDashIndex >= 0 && lastDashIndex + 1 < fileName.Length)
                return fileName[(lastDashIndex + 1)..];
        }

        var titleLastDashIndex = summaryTitle.LastIndexOf('-');

        return titleLastDashIndex >= 0 && titleLastDashIndex + 1 < summaryTitle.Length
            ? summaryTitle[(titleLastDashIndex + 1)..]
            : null;
    }
}
