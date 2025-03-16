using System.IO.Pipelines;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Atom.Text.Json.Tests;

public class JsonContextTypeSyntaxProviderTests(ILogger logger) : BenchmarkTest<JsonContextTypeSyntaxProviderTests>(logger)
{
    private static string? source;
    private static string? reference;
    private static string? derivedReference;

    public override bool IsBenchmarkDisabled => true;

    public JsonContextTypeSyntaxProviderTests() : this(ConsoleLogger.Unicode) { }

    private static void Settings()
    {
        if (string.IsNullOrEmpty(source) && File.Exists("assets/json.source"))
            source = File.ReadAllText("assets/json.source");

        if (string.IsNullOrEmpty(reference) && File.Exists("assets/json.reference"))
            reference = File.ReadAllText("assets/json.reference");

        if (string.IsNullOrEmpty(derivedReference) && File.Exists("assets/json_derived.reference"))
            derivedReference = File.ReadAllText("assets/json_derived.reference");
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

    [TestCase(TestName = "Тест анализатора контекстов сериализации"), Benchmark]
    public async Task AnalyzerTest()
    {
        var test = new CSharpAnalyzerTest<JsonContextSourceAnalyzer, DefaultVerifier>
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
            .WithSpan(16, 2, 20, 2)
            .WithMessage("Обнаружен атрибут 'JsonContext'")
        );

        test.ExpectedDiagnostics.Add(new DiagnosticResult("A0001", DiagnosticSeverity.Hidden)
            .WithSpan(219, 2, 223, 2)
            .WithMessage("Обнаружен атрибут 'JsonContext'")
        );

        await test.RunAsync();
        if (IsTest) Assert.Pass();
    }

    [TestCase(TestName = "Тест генератора контекстов сериализации"), Benchmark]
    public async Task GeneratorTest()
    {
        var test = new CSharpSourceGeneratorTest<JsonContextSourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
                Sources = { source! },
                GeneratedSources = {
                    (typeof(JsonContextSourceGenerator), "Proxy.Json.g.cs", reference!),
                    (typeof(JsonContextSourceGenerator), "ServiceProxy.Json.g.cs", derivedReference!),
                },
                AdditionalReferences =
                {
                    MetadataReference.CreateFromFile(typeof(JsonSourceGenerationOptionsAttribute).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(PipeWriter).Assembly.Location),
                    MetadataReference.CreateFromFile(Path.Combine(Directory.GetCurrentDirectory(), "Escorp.Atom.dll")),
                },
            }
        };

        await test.RunAsync();
        if (IsTest) Assert.Pass();
    }
}