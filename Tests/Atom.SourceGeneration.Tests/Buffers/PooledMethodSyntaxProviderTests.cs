using Atom.SourceGeneration.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Atom.Buffers.Tests;

[Parallelizable(ParallelScope.All)]
public class PooledMethodSyntaxProviderTests(ILogger logger) : BenchmarkTests<PooledMethodSyntaxProviderTests>(logger)
{
    private static string? source;
    private static string? reference;

    public PooledMethodSyntaxProviderTests() : this(ConsoleLogger.Unicode) { }

    private static void Settings()
    {
        if (string.IsNullOrEmpty(source) && File.Exists("assets/pooled.source"))
            source = File.ReadAllText("assets/pooled.source");

        if (string.IsNullOrEmpty(reference) && File.Exists("assets/pooled.reference"))
            reference = File.ReadAllText("assets/pooled.reference");
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

    [TestCase(TestName = "Тест анализатора буферизации"), Benchmark]
    public async Task AnalyzerTestAsync()
    {
        var test = new CSharpAnalyzerTest<PooledSourceAnalyzer, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                Sources = { source! },
                AdditionalReferences =
                {
                    MetadataReference.CreateFromFile(Path.Combine(Directory.GetCurrentDirectory(), "Escorp.Atom.dll")),
                },
            },
        };

        test.ExpectedDiagnostics.Add(new DiagnosticResult("A0001", DiagnosticSeverity.Hidden)
            .WithSpan(11, 6, 11, 12)
            .WithMessage("Обнаружен атрибут 'Pooled'")
        );

        await test.RunAsync();
        if (!IsBenchmarkEnabled) Assert.Pass();
    }

    [TestCase(TestName = "Тест генератора буферизации"), Benchmark]
    public async Task GeneratorTestAsync()
    {
        var test = new CSharpSourceGeneratorTest<PooledSourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                Sources = { source! },
                GeneratedSources = { (typeof(PooledSourceGenerator), "Test.Pooled.g.cs", reference!) },
                AdditionalReferences =
                {
                    MetadataReference.CreateFromFile(Path.Combine(Directory.GetCurrentDirectory(), "Escorp.Atom.dll")),
                },
            }
        };

        await test.RunAsync();
        if (!IsBenchmarkEnabled) Assert.Pass();
    }
}