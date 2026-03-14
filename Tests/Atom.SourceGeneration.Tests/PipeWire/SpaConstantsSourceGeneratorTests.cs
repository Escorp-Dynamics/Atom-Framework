using Atom.SourceGeneration.PipeWire;
using Atom.SourceGeneration.Tests;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Atom.SourceGeneration.PipeWire.Tests;

[Parallelizable(ParallelScope.All)]
public class SpaConstantsSourceGeneratorTests(ILogger logger) : BenchmarkTests<SpaConstantsSourceGeneratorTests>(logger)
{
    private static string? spaType;
    private static string? spaSubtype;
    private static string? reference;

    public SpaConstantsSourceGeneratorTests() : this(ConsoleLogger.Unicode) { }

    private static void Settings()
    {
        if (string.IsNullOrEmpty(spaType) && File.Exists("assets/spa_type.h"))
            spaType = File.ReadAllText("assets/spa_type.h");

        if (string.IsNullOrEmpty(spaSubtype) && File.Exists("assets/spa_subtype.h"))
            spaSubtype = File.ReadAllText("assets/spa_subtype.h");

        if (string.IsNullOrEmpty(reference) && File.Exists("assets/spa_constants.reference"))
            reference = File.ReadAllText("assets/spa_constants.reference");
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

    [TestCase(TestName = "Тест генератора SPA констант из заголовочных файлов PipeWire"), Benchmark]
    public async Task GeneratorTestAsync()
    {
        var test = new CSharpSourceGeneratorTest<SpaConstantsSourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                AdditionalFiles =
                {
                    ("spa_subtype.h", spaSubtype!),
                    ("spa_type.h", spaType!),
                },
                GeneratedSources =
                {
                    (typeof(SpaConstantsSourceGenerator), "SpaConstants.g.cs", reference!),
                },
            },
        };

        await test.RunAsync();
        if (!IsBenchmarkEnabled) Assert.Pass();
    }
}
