using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Atom.Architect.Components.Tests;

public class ComponentTypeSyntaxProviderTests(ILogger logger) : BenchmarkTests<ComponentTypeSyntaxProviderTests>(logger)
{
    private static string? source;
    private static string? reference1;
    private static string? reference2;

    public ComponentTypeSyntaxProviderTests() : this(ConsoleLogger.Unicode) { }

    private static void Settings()
    {
        if (string.IsNullOrEmpty(source) && File.Exists("assets/component.source"))
            source = File.ReadAllText("assets/component.source");

        if (string.IsNullOrEmpty(reference1) && File.Exists("assets/component1.reference"))
            reference1 = File.ReadAllText("assets/component1.reference");

        if (string.IsNullOrEmpty(reference2) && File.Exists("assets/component2.reference"))
            reference2 = File.ReadAllText("assets/component2.reference");
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

    [TestCase(TestName = "Тест анализатора компонентов"), Benchmark]
    public async Task AnalyzerTest()
    {
        var test = new CSharpAnalyzerTest<ComponentSourceAnalyzer, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
                Sources = { source! },
                AdditionalReferences =
                {
                    MetadataReference.CreateFromFile(Path.Combine(Directory.GetCurrentDirectory(), "Escorp.Atom.dll")),
                },
            },
        };

        test.ExpectedDiagnostics.Add(new DiagnosticResult("A0001", DiagnosticSeverity.Hidden)
            .WithSpan(5, 2, 5, 11)
            .WithMessage("Обнаружен атрибут 'Component'")
        );

        test.ExpectedDiagnostics.Add(new DiagnosticResult("A0001", DiagnosticSeverity.Hidden)
            .WithSpan(8, 2, 8, 11)
            .WithMessage("Обнаружен атрибут 'Component'")
        );

        await test.RunAsync();
        if (!IsBenchmarkEnabled) Assert.Pass();
    }

    [TestCase(TestName = "Тест генератора компонентов"), Benchmark]
    public async Task GeneratorTest()
    {
        var test = new CSharpSourceGeneratorTest<ComponentSourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
                Sources = { source! },
                GeneratedSources = {
                    (typeof(ComponentSourceGenerator), "Component1.Component.g.cs", reference1!),
                    (typeof(ComponentSourceGenerator), "Component2.Component.g.cs", reference2!),
                },
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