using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Atom.Compilers.JavaScript.SourceGeneration.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class JavaScriptDictionarySourceGeneratorTests
{
    private const string Source = """
using Atom.Compilers.JavaScript;

namespace Demo;

[JavaScriptDictionary(IsStringKeysOnly = true)]
public sealed class HeaderMap
{
}
""";

    private const string Reference = """
namespace Demo;

[global::System.CodeDom.Compiler.GeneratedCode("Escorp.Atom.Compilers.JavaScript.SourceGeneration", "0.0.1")]
internal static class HeaderMapJavaScriptDictionaryMetadata
{
    internal const string EntityName = "HeaderMap";
    internal const string Generator = "JavaScriptDictionary";
    internal const int MetadataVersion = 1;
    internal const int AnnotatedMemberCount = 1;
    internal const string Member0Name = "HeaderMap";
    internal const string Member0Kind = "Class";
    internal const string Member0ExportName = "HeaderMap";
    internal const bool IsStringKeysOnly = true;
    internal const bool IsPreserveEnumerationOrder = true;
}

""";

    [Test]
    public async Task GeneratorTestAsync()
    {
        var test = new CSharpSourceGeneratorTest<JavaScriptDictionarySourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                Sources = { Source },
                GeneratedSources = { (typeof(JavaScriptDictionarySourceGenerator), "HeaderMapJavaScriptDictionaryMetadata.g.cs", Reference) },
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
    public async Task StructGeneratorTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

[JavaScriptDictionary(IsStringKeysOnly = true)]
public readonly struct HeaderMap
{
}
""";

        const string reference = """
namespace Demo;

[global::System.CodeDom.Compiler.GeneratedCode("Escorp.Atom.Compilers.JavaScript.SourceGeneration", "0.0.1")]
internal static class HeaderMapJavaScriptDictionaryMetadata
{
    internal const string EntityName = "HeaderMap";
    internal const string Generator = "JavaScriptDictionary";
    internal const int MetadataVersion = 1;
    internal const int AnnotatedMemberCount = 1;
    internal const string Member0Name = "HeaderMap";
    internal const string Member0Kind = "Struct";
    internal const string Member0ExportName = "HeaderMap";
    internal const bool IsStringKeysOnly = true;
    internal const bool IsPreserveEnumerationOrder = true;
}

""";

        var test = new CSharpSourceGeneratorTest<JavaScriptDictionarySourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                Sources = { source },
                GeneratedSources = { (typeof(JavaScriptDictionarySourceGenerator), "HeaderMapJavaScriptDictionaryMetadata.g.cs", reference) },
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
    public async Task CustomFlagsGeneratorTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

[JavaScriptDictionary(IsStringKeysOnly = false, IsPreserveEnumerationOrder = false)]
public sealed class HeaderMap
{
}
""";

        const string reference = """
namespace Demo;

[global::System.CodeDom.Compiler.GeneratedCode("Escorp.Atom.Compilers.JavaScript.SourceGeneration", "0.0.1")]
internal static class HeaderMapJavaScriptDictionaryMetadata
{
    internal const string EntityName = "HeaderMap";
    internal const string Generator = "JavaScriptDictionary";
    internal const int MetadataVersion = 1;
    internal const int AnnotatedMemberCount = 1;
    internal const string Member0Name = "HeaderMap";
    internal const string Member0Kind = "Class";
    internal const string Member0ExportName = "HeaderMap";
    internal const bool IsStringKeysOnly = false;
    internal const bool IsPreserveEnumerationOrder = false;
}

""";

        var test = new CSharpSourceGeneratorTest<JavaScriptDictionarySourceGenerator, DefaultVerifier>
        {
            TestState =
            {
                ReferenceAssemblies = TestReferenceAssemblies.Net10_0,
                Sources = { source },
                GeneratedSources = { (typeof(JavaScriptDictionarySourceGenerator), "HeaderMapJavaScriptDictionaryMetadata.g.cs", reference) },
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