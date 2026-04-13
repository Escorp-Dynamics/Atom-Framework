using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Atom.Compilers.JavaScript.SourceGeneration.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class JavaScriptFunctionSourceGeneratorTests
{
    private const string Source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptFunction("sum", IsPure = true)]
    public int Sum(int left, int right) => left + right;

    [JavaScriptFunction("mul", IsInline = false)]
    public int Multiply(int left, int right) => left * right;
}
""";

    private const string Reference = """
namespace Demo;

[global::System.CodeDom.Compiler.GeneratedCode("Escorp.Atom.Compilers.JavaScript.SourceGeneration", "0.0.1")]
internal static class HostBridgeJavaScriptFunctionMetadata
{
    internal const string EntityName = "HostBridge";
    internal const string Generator = "JavaScriptFunction";
    internal const int MetadataVersion = 1;
    internal const int AnnotatedMemberCount = 2;
    internal const string Member0Name = "Sum";
    internal const string Member0Kind = "Method";
    internal const string Member0ExportName = "sum";
    internal const bool Member0IsPure = true;
    internal const bool Member0IsInline = true;
    internal const string Member1Name = "Multiply";
    internal const string Member1Kind = "Method";
    internal const string Member1ExportName = "mul";
    internal const bool Member1IsPure = false;
    internal const bool Member1IsInline = false;
}

""";

    [Test]
    public async Task GeneratorTestAsync()
    {
        var test = new CSharpSourceGeneratorTest<JavaScriptFunctionSourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                Sources = { Source },
                GeneratedSources = { (typeof(JavaScriptFunctionSourceGenerator), "HostBridgeJavaScriptFunctionMetadata.g.cs", Reference) },
                AdditionalReferences =
                {
                    JavaScriptTestReferences.Atom,
                    JavaScriptTestReferences.Runtime,
                },
            },
        };

        await test.RunAsync();
    }
}