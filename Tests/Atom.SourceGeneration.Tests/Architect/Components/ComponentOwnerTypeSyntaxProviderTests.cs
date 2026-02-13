using Atom.SourceGeneration.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Atom.Architect.Components.Tests;

[Parallelizable(ParallelScope.All)]
public class ComponentOwnerTypeSyntaxProviderTests(ILogger logger) : BenchmarkTests<ComponentOwnerTypeSyntaxProviderTests>(logger)
{
    private static string? source;
    private static string? reference;

    public ComponentOwnerTypeSyntaxProviderTests() : this(ConsoleLogger.Unicode) { }

    private static void Settings()
    {
        if (string.IsNullOrEmpty(source) && File.Exists("assets/component_owner.source"))
            source = File.ReadAllText("assets/component_owner.source");

        if (string.IsNullOrEmpty(reference) && File.Exists("assets/component_owner.reference"))
            reference = File.ReadAllText("assets/component_owner.reference");
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

    [TestCase(TestName = "Тест анализатора владельцев компонентов"), Benchmark]
    public async Task AnalyzerTestAsync()
    {
        var test = new CSharpAnalyzerTest<ComponentOwnerSourceAnalyzer, DefaultVerifier>
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
            .WithSpan(119, 2, 119, 36)
            .WithMessage("Обнаружен атрибут 'ComponentOwner'")
        );

        test.ExpectedDiagnostics.Add(new DiagnosticResult("A0001", DiagnosticSeverity.Hidden)
            .WithSpan(120, 2, 120, 36)
            .WithMessage("Обнаружен атрибут 'ComponentOwner'")
        );

        await test.RunAsync();
        if (!IsBenchmarkEnabled) Assert.Pass();
    }

    [TestCase(TestName = "Тест генератора владельцев компонентов"), Benchmark]
    public async Task GeneratorTestAsync()
    {
        var test = new CSharpSourceGeneratorTest<ComponentOwnerSourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                Sources = { source! },
                GeneratedSources = { (typeof(ComponentOwnerSourceGenerator), "TestFactory.ComponentOwner.g.cs", reference!) },
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