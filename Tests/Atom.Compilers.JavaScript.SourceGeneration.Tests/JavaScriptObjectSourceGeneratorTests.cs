using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Atom.Compilers.JavaScript.SourceGeneration.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class JavaScriptObjectSourceGeneratorTests
{
    private const string Source = """
using Atom.Compilers.JavaScript;

namespace Demo;

[JavaScriptObject("host")]
public sealed class HostBridge
{
}
""";

    private const string Reference = """
namespace Demo;

[global::System.CodeDom.Compiler.GeneratedCode("Escorp.Atom.Compilers.JavaScript.SourceGeneration", "0.0.1")]
internal static class HostBridgeJavaScriptObjectMetadata
{
    internal const string EntityName = "HostBridge";
    internal const string Generator = "JavaScriptObject";
    internal const int MetadataVersion = 1;
    internal const int AnnotatedMemberCount = 1;
    internal const string Member0Name = "HostBridge";
    internal const string Member0Kind = "Class";
    internal const string Member0ExportName = "host";
    internal const bool IsGlobalExportEnabled = false;
}

""";

    [Test]
    public async Task GeneratorTestAsync()
    {
        var test = new CSharpSourceGeneratorTest<JavaScriptObjectSourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                Sources = { Source },
                GeneratedSources = { (typeof(JavaScriptObjectSourceGenerator), "HostBridgeJavaScriptObjectMetadata.g.cs", Reference) },
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
    public async Task InterfaceGeneratorTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

[JavaScriptObject]
public interface IHostBridge
{
}
""";

        const string reference = """
namespace Demo;

[global::System.CodeDom.Compiler.GeneratedCode("Escorp.Atom.Compilers.JavaScript.SourceGeneration", "0.0.1")]
internal static class IHostBridgeJavaScriptObjectMetadata
{
    internal const string EntityName = "IHostBridge";
    internal const string Generator = "JavaScriptObject";
    internal const int MetadataVersion = 1;
    internal const int AnnotatedMemberCount = 1;
    internal const string Member0Name = "IHostBridge";
    internal const string Member0Kind = "Interface";
    internal const string Member0ExportName = "IHostBridge";
    internal const bool IsGlobalExportEnabled = false;
}

""";

        var test = new CSharpSourceGeneratorTest<JavaScriptObjectSourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                Sources = { source },
                GeneratedSources = { (typeof(JavaScriptObjectSourceGenerator), "IHostBridgeJavaScriptObjectMetadata.g.cs", reference) },
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
    public async Task NamespaceCollisionProducesDiagnosticAsync()
    {
        const string firstSource = """
using Atom.Compilers.JavaScript;

namespace Demo.A;

[JavaScriptObject("a")]
public sealed class HostBridge
{
}
""";

        const string secondSource = """
using Atom.Compilers.JavaScript;

namespace Demo.B;

[JavaScriptObject("b")]
public sealed class HostBridge
{
}
""";

        var test = new CSharpSourceGeneratorTest<JavaScriptObjectSourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                Sources = { firstSource, secondSource },
                AdditionalReferences =
                {
                    JavaScriptTestReferences.Atom,
                    JavaScriptTestReferences.Runtime,
                },
            },
        };

        test.ExpectedDiagnostics.Add(new DiagnosticResult("A1001", DiagnosticSeverity.Error)
            .WithSpan(5, 1, 8, 2)
            .WithMessage("Generator не может агрегировать entity 'HostBridge' из нескольких разных type identities: Demo.A.HostBridge, Demo.B.HostBridge"));

        await test.RunAsync();
    }

    [Test]
    public async Task NestedCollisionProducesDiagnosticAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class OuterA
{
    [JavaScriptObject("a")]
    public sealed class HostBridge
    {
    }
}

public sealed class OuterB
{
    [JavaScriptObject("b")]
    public sealed class HostBridge
    {
    }
}
""";

        var test = new CSharpSourceGeneratorTest<JavaScriptObjectSourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                Sources = { source },
                AdditionalReferences =
                {
                    JavaScriptTestReferences.Atom,
                    JavaScriptTestReferences.Runtime,
                },
            },
        };

        test.ExpectedDiagnostics.Add(new DiagnosticResult("A1001", DiagnosticSeverity.Error)
            .WithSpan(7, 5, 10, 6)
            .WithMessage("Generator не может агрегировать entity 'HostBridge' из нескольких разных type identities: Demo.OuterA.HostBridge, Demo.OuterB.HostBridge"));

        await test.RunAsync();
    }
}