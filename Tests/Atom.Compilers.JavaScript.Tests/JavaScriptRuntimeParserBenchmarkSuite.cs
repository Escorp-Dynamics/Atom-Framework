using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace Atom.Compilers.JavaScript.Tests;

[MemoryDiagnoser]
[ThreadingDiagnoser]
[ShortRunJob(RuntimeMoniker.Net10_0)]
public class JavaScriptRuntimeParserBenchmarkSuite
{
    private string aggregateMixedExpression = null!;
    private string largeArrayLiteralExpression = null!;
    private string largeObjectLiteralExpression = null!;
    private JavaScriptRuntimeExecutionState executionState;
    private string arraySpreadExpression = null!;
    private string conditionalExpression = null!;
    private string destructuringExpression = null!;
    private string objectLiteralExpression = null!;
    private string shortExpression = null!;
    private string regexHeavyExpression = null!;
    private string templateHeavyExpression = null!;

    [GlobalSetup]
    public void GlobalSetUp()
    {
        var runtime = new JavaScriptRuntime();
        _ = runtime.Execute(ReadOnlySpan<char>.Empty);
        executionState = runtime.CurrentExecutionState;
        runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();

        shortExpression = "host.connect(left, right ?? fallback) ? left.value : right?.value";
        regexHeavyExpression = "return /[a/b]{2,4}\\d+/g.test(value) ? source[index] : fallback[index + 1]";
        templateHeavyExpression = "`${/}/.test(value) ? host.connect() : `${left ?? right}`}`";
        arraySpreadExpression = "[head, ...tail, source[index ?? 0], ...(fallback ?? backup)]";
        conditionalExpression = "flag && source.ready ? values[index + 1] : fallback?.value ?? backup";
        destructuringExpression = "({ left, right: alias = fallback, nested: { value } } = source)";
        objectLiteralExpression = "({ left, right: fallback ?? source.value, nested: { ok: true, item: values[index] } })";
        largeArrayLiteralExpression = "[alpha, beta, gamma, delta, epsilon, zeta, eta, theta, ...(left ?? []), ...(right ?? []), source[index], fallback[index + 1], [nested0, nested1, nested2, nested3]]";
        largeObjectLiteralExpression = "({ alpha, beta, gamma, delta, epsilon, nested: { left, right, fallback, values: [first, second, third], flags: { ready: true, active: flag } }, extra: source?.value ?? backup, index: values[index + 1] })";
        aggregateMixedExpression = "({ items: [first, second, third, ...rest, ...(tail ?? [])], meta: { left, right, alias: fallback ?? backup, nested: { a: one, b: two, c: three } }, selected: flag ? source[index] : fallback[index + 1] })";
    }

    [Benchmark(Description = "Parser short expression", Baseline = true)]
    public void ParseShortExpression()
        => Parse(shortExpression);

    [Benchmark(Description = "Parser regex-heavy expression")]
    public void ParseRegexHeavyExpression()
        => Parse(regexHeavyExpression);

    [Benchmark(Description = "Parser template-heavy expression")]
    public void ParseTemplateHeavyExpression()
        => Parse(templateHeavyExpression);

    [Benchmark(Description = "Parser array-spread expression")]
    public void ParseArraySpreadExpression()
        => Parse(arraySpreadExpression);

    [Benchmark(Description = "Parser conditional expression")]
    public void ParseConditionalExpression()
        => Parse(conditionalExpression);

    [Benchmark(Description = "Parser destructuring expression")]
    public void ParseDestructuringExpression()
        => Parse(destructuringExpression);

    [Benchmark(Description = "Parser object-literal expression")]
    public void ParseObjectLiteralExpression()
        => Parse(objectLiteralExpression);

    [Benchmark(Description = "Parser large-array-literal expression")]
    public void ParseLargeArrayLiteralExpression()
        => Parse(largeArrayLiteralExpression);

    [Benchmark(Description = "Parser large-object-literal expression")]
    public void ParseLargeObjectLiteralExpression()
        => Parse(largeObjectLiteralExpression);

    [Benchmark(Description = "Parser aggregate-mixed expression")]
    public void ParseAggregateMixedExpression()
        => Parse(aggregateMixedExpression);

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
}