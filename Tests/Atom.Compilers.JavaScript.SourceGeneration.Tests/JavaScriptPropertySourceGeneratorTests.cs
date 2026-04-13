using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Atom.Compilers.JavaScript.SourceGeneration.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class JavaScriptPropertySourceGeneratorTests
{
    private const string Source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty("count", IsRequired = true)]
    public int Count { get; set; }
}
""";

    private const string Reference = """
namespace Demo;

[global::System.CodeDom.Compiler.GeneratedCode("Escorp.Atom.Compilers.JavaScript.SourceGeneration", "0.0.1")]
internal static class HostBridgeJavaScriptPropertyMetadata
{
    internal const string EntityName = "HostBridge";
    internal const string Generator = "JavaScriptProperty";
    internal const int MetadataVersion = 1;
    internal const int AnnotatedMemberCount = 1;
    internal const string Member0Name = "Count";
    internal const string Member0Kind = "Property";
    internal const string Member0ExportName = "count";
    internal const bool Member0IsReadOnly = false;
    internal const bool Member0IsRequired = true;
}

""";

    [Test]
    public async Task GeneratorTestAsync()
    {
        var test = new CSharpSourceGeneratorTest<JavaScriptPropertySourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                Sources = { Source },
                GeneratedSources = { (typeof(JavaScriptPropertySourceGenerator), "HostBridgeJavaScriptPropertyMetadata.g.cs", Reference) },
                AdditionalReferences =
                {
                    JavaScriptTestReferences.Atom,
                    JavaScriptTestReferences.Runtime,
                },
            },
        };

        await test.RunAsync();
    }

    [Test]
    public async Task MultiPropertyAggregationTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty("count")]
    public int Count { get; set; }

    [JavaScriptProperty("total")]
    public int Total { get; set; }
}
""";

        const string reference = """
namespace Demo;

[global::System.CodeDom.Compiler.GeneratedCode("Escorp.Atom.Compilers.JavaScript.SourceGeneration", "0.0.1")]
internal static class HostBridgeJavaScriptPropertyMetadata
{
    internal const string EntityName = "HostBridge";
    internal const string Generator = "JavaScriptProperty";
    internal const int MetadataVersion = 1;
    internal const int AnnotatedMemberCount = 2;
    internal const string Member0Name = "Count";
    internal const string Member0Kind = "Property";
    internal const string Member0ExportName = "count";
    internal const bool Member0IsReadOnly = false;
    internal const bool Member0IsRequired = false;
    internal const string Member1Name = "Total";
    internal const string Member1Kind = "Property";
    internal const string Member1ExportName = "total";
    internal const bool Member1IsReadOnly = false;
    internal const bool Member1IsRequired = false;
}

""";

        var test = new CSharpSourceGeneratorTest<JavaScriptPropertySourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                Sources = { source },
                GeneratedSources = { (typeof(JavaScriptPropertySourceGenerator), "HostBridgeJavaScriptPropertyMetadata.g.cs", reference) },
                AdditionalReferences =
                {
                    JavaScriptTestReferences.Atom,
                    JavaScriptTestReferences.Runtime,
                },
            },
        };

        await test.RunAsync();
    }

    [Test]
    public async Task PartialTypeAggregationOrderingTestAsync()
    {
        const string firstSource = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed partial class HostBridge
{
    [JavaScriptProperty("count")]
    public int Count { get; set; }
}
""";

        const string secondSource = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed partial class HostBridge
{
    [JavaScriptProperty("total")]
    public int Total { get; set; }
}
""";

        const string reference = """
namespace Demo;

[global::System.CodeDom.Compiler.GeneratedCode("Escorp.Atom.Compilers.JavaScript.SourceGeneration", "0.0.1")]
internal static class HostBridgeJavaScriptPropertyMetadata
{
    internal const string EntityName = "HostBridge";
    internal const string Generator = "JavaScriptProperty";
    internal const int MetadataVersion = 1;
    internal const int AnnotatedMemberCount = 2;
    internal const string Member0Name = "Count";
    internal const string Member0Kind = "Property";
    internal const string Member0ExportName = "count";
    internal const bool Member0IsReadOnly = false;
    internal const bool Member0IsRequired = false;
    internal const string Member1Name = "Total";
    internal const string Member1Kind = "Property";
    internal const string Member1ExportName = "total";
    internal const bool Member1IsReadOnly = false;
    internal const bool Member1IsRequired = false;
}

""";

        var test = new CSharpSourceGeneratorTest<JavaScriptPropertySourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                Sources = { firstSource, secondSource },
                GeneratedSources = { (typeof(JavaScriptPropertySourceGenerator), "HostBridgeJavaScriptPropertyMetadata.g.cs", reference) },
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