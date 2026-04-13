using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Atom.Compilers.JavaScript.SourceGeneration.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class JavaScriptIgnoreSourceGeneratorTests
{
    private const string Source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptIgnore]
    public int CachedValue { get; set; }

    [JavaScriptIgnore]
    public int Compute(int value) => value;
}
""";

    private const string MultiFieldSource = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptIgnore]
    private int left, right;
}
""";

    private const string IndexerEventSource = """
using System;
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptIgnore]
    public int this[int index]
    {
        get => index;
        set { }
    }

    [JavaScriptIgnore]
    public event Action? Changed;
}
""";

    private const string TypeSource = """
using Atom.Compilers.JavaScript;

namespace Demo;

[JavaScriptIgnore]
public sealed class HostBridge
{
}
""";

    private const string Reference = """
namespace Demo;

[global::System.CodeDom.Compiler.GeneratedCode("Escorp.Atom.Compilers.JavaScript.SourceGeneration", "0.0.1")]
internal static class HostBridgeJavaScriptIgnoreMetadata
{
    internal const string EntityName = "HostBridge";
    internal const string Generator = "JavaScriptIgnore";
    internal const int MetadataVersion = 1;
    internal const int AnnotatedMemberCount = 2;
    internal const string Member0Name = "CachedValue";
    internal const string Member0Kind = "Property";
    internal const string Member1Name = "Compute";
    internal const string Member1Kind = "Method";
}

""";

    private const string MultiFieldReference = """
namespace Demo;

[global::System.CodeDom.Compiler.GeneratedCode("Escorp.Atom.Compilers.JavaScript.SourceGeneration", "0.0.1")]
internal static class HostBridgeJavaScriptIgnoreMetadata
{
    internal const string EntityName = "HostBridge";
    internal const string Generator = "JavaScriptIgnore";
    internal const int MetadataVersion = 1;
    internal const int AnnotatedMemberCount = 2;
    internal const string Member0Name = "left";
    internal const string Member0Kind = "Field";
    internal const string Member1Name = "right";
    internal const string Member1Kind = "Field";
}

""";

    private const string IndexerEventReference = """
namespace Demo;

[global::System.CodeDom.Compiler.GeneratedCode("Escorp.Atom.Compilers.JavaScript.SourceGeneration", "0.0.1")]
internal static class HostBridgeJavaScriptIgnoreMetadata
{
    internal const string EntityName = "HostBridge";
    internal const string Generator = "JavaScriptIgnore";
    internal const int MetadataVersion = 1;
    internal const int AnnotatedMemberCount = 2;
    internal const string Member0Name = "Item";
    internal const string Member0Kind = "Indexer";
    internal const string Member1Name = "Changed";
    internal const string Member1Kind = "Event";
}

""";

    private const string TypeReference = """
namespace Demo;

[global::System.CodeDom.Compiler.GeneratedCode("Escorp.Atom.Compilers.JavaScript.SourceGeneration", "0.0.1")]
internal static class HostBridgeJavaScriptIgnoreMetadata
{
    internal const string EntityName = "HostBridge";
    internal const string Generator = "JavaScriptIgnore";
    internal const int MetadataVersion = 1;
    internal const int AnnotatedMemberCount = 1;
    internal const string Member0Name = "HostBridge";
    internal const string Member0Kind = "Class";
}

""";

    [Test]
    public async Task GeneratorTestAsync()
    {
        var test = new CSharpSourceGeneratorTest<JavaScriptIgnoreSourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                Sources = { Source },
                GeneratedSources = { (typeof(JavaScriptIgnoreSourceGenerator), "HostBridgeJavaScriptIgnoreMetadata.g.cs", Reference) },
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
    public async Task TypeGeneratorTestAsync()
    {
        var test = new CSharpSourceGeneratorTest<JavaScriptIgnoreSourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                Sources = { TypeSource },
                GeneratedSources = { (typeof(JavaScriptIgnoreSourceGenerator), "HostBridgeJavaScriptIgnoreMetadata.g.cs", TypeReference) },
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
    public async Task MultiFieldGeneratorTestAsync()
    {
        var test = new CSharpSourceGeneratorTest<JavaScriptIgnoreSourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                Sources = { MultiFieldSource },
                GeneratedSources = { (typeof(JavaScriptIgnoreSourceGenerator), "HostBridgeJavaScriptIgnoreMetadata.g.cs", MultiFieldReference) },
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
    public async Task IndexerAndEventGeneratorTestAsync()
    {
        var test = new CSharpSourceGeneratorTest<JavaScriptIgnoreSourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                Sources = { IndexerEventSource },
                GeneratedSources = { (typeof(JavaScriptIgnoreSourceGenerator), "HostBridgeJavaScriptIgnoreMetadata.g.cs", IndexerEventReference) },
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