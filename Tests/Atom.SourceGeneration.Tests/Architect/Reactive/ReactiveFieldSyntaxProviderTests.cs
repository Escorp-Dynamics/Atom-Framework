using Atom.SourceGeneration.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Atom.Architect.Reactive.Tests;

[Parallelizable(ParallelScope.All)]
public class ReactiveFieldSyntaxProviderTests(ILogger logger) : BenchmarkTests<ReactiveFieldSyntaxProviderTests>(logger)
{
    private static string? source;
    private static string? reference;

    public ReactiveFieldSyntaxProviderTests() : this(ConsoleLogger.Unicode) { }

    private static void Settings()
    {
        if (string.IsNullOrEmpty(source) && File.Exists("assets/reactively.source"))
            source = File.ReadAllText("assets/reactively.source");

        if (string.IsNullOrEmpty(reference) && File.Exists("assets/reactively.reference"))
            reference = File.ReadAllText("assets/reactively.reference");
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

    [TestCase(TestName = "Тест анализатора реактивных свойств"), Benchmark]
    public async Task AnalyzerTestAsync()
    {
        var test = new CSharpAnalyzerTest<ReactivelySourceAnalyzer, DefaultVerifier>
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
            .WithSpan(15, 6, 15, 59)
            .WithMessage("Обнаружен атрибут 'Reactively'")
        );

        test.ExpectedDiagnostics.Add(new DiagnosticResult("A0001", DiagnosticSeverity.Hidden)
            .WithSpan(21, 6, 21, 16)
            .WithMessage("Обнаружен атрибут 'Reactively'")
        );

        test.ExpectedDiagnostics.Add(new DiagnosticResult("A0001", DiagnosticSeverity.Hidden)
            .WithSpan(25, 6, 25, 16)
            .WithMessage("Обнаружен атрибут 'Reactively'")
        );

        await test.RunAsync();
        if (!IsBenchmarkEnabled) Assert.Pass();
    }

    [TestCase(TestName = "Тест генератора реактивных свойств"), Benchmark]
    public async Task GeneratorTestAsync()
    {
        var test = new CSharpSourceGeneratorTest<ReactivelySourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                Sources = { source! },
                GeneratedSources = { (typeof(ReactivelySourceGenerator), "VirtualCamera.Reactively.g.cs", reference!) },
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