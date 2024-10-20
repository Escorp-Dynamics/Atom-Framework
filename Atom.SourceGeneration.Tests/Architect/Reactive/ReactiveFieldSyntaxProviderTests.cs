using Atom.Architect.Reactive;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Atom.SourceGeneration.Architect.Reactive.Tests;

public class ReactiveFieldSyntaxProviderTests(ILogger logger) : BenchmarkTest<ReactiveFieldSyntaxProviderTests>(logger)
{
    private static string? reactivelySource;
    private static string? reactivelyReference;

    public override bool IsBenchmarkDisabled => true;

    public ReactiveFieldSyntaxProviderTests() : this(ConsoleLogger.Unicode) { }

    private static void Settings()
    {
        if (string.IsNullOrEmpty(reactivelySource) && File.Exists("assets/reactively.source"))
            reactivelySource = File.ReadAllText("assets/reactively.source");

        if (string.IsNullOrEmpty(reactivelyReference) && File.Exists("assets/reactively.reference"))
            reactivelyReference = File.ReadAllText("assets/reactively.reference");
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
    public async Task AnalyzerTest()
    {
        var test = new CSharpAnalyzerTest<ReactivelySourceAnalyzer, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
                Sources = { reactivelySource! },
                AdditionalReferences =
                {
                    MetadataReference.CreateFromFile(Path.Combine(Directory.GetCurrentDirectory(), "Escorp.Atom.dll")),
                },
            },
            DisabledDiagnostics = { "CS0234", "CS0246", "CS0103" }
        };

        test.ExpectedDiagnostics.Add(new DiagnosticResult("A0001", DiagnosticSeverity.Hidden)
            .WithSpan(15, 6, 15, 59)
            .WithMessage("Обнаружен атрибут 'Reactively'")
        );

        test.ExpectedDiagnostics.Add(new DiagnosticResult("A0001", DiagnosticSeverity.Hidden)
            .WithSpan(21, 6, 21, 16)
            .WithMessage("Обнаружен атрибут 'Reactively'")
        );

        await test.RunAsync();
        if (IsTest) Assert.Pass();
    }

    [TestCase(TestName = "Тест генератора реактивных свойств"), Benchmark]
    public async Task GeneratorTest()
    {
        var test = new CSharpSourceGeneratorTest<ReactivelySourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
                Sources = { reactivelySource! },
                GeneratedSources = { (typeof(ReactivelySourceGenerator), "VirtualCamera.Reactively.g.cs", reactivelyReference!) },
                AdditionalReferences =
                {
                    MetadataReference.CreateFromFile(Path.Combine(Directory.GetCurrentDirectory(), "Escorp.Atom.dll")),
                },
            }
        };

        await test.RunAsync();
        if (IsTest) Assert.Pass();
    }
}