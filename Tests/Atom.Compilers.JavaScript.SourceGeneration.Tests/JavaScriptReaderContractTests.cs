using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Atom.Compilers.JavaScript.SourceGeneration.Tests;

[Parallelizable(ParallelScope.All)]
public sealed partial class JavaScriptReaderContractTests
{
    [Test]
    public void ObjectMetadataReaderContractTest()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

[JavaScriptObject("host", IsGlobalExportEnabled = true)]
public sealed class HostBridge
{
}
""";

        var generated = RunGenerator<JavaScriptObjectSourceGenerator>(source, "HostBridgeJavaScriptObjectMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.EntityName, Is.EqualTo("HostBridge"));
            Assert.That(metadata.Generator, Is.EqualTo("JavaScriptObject"));
            Assert.That(metadata.MetadataVersion, Is.EqualTo(1));
            Assert.That(metadata.IsGlobalExportEnabled, Is.True);
            Assert.That(metadata.Members.Length, Is.EqualTo(1));
            Assert.That(metadata.Members[0].Name, Is.EqualTo("HostBridge"));
            Assert.That(metadata.Members[0].Kind, Is.EqualTo("Class"));
            Assert.That(metadata.Members[0].ExportName, Is.EqualTo("host"));
        });
    }

    [Test]
    public void ObjectInterfaceMetadataReaderContractTest()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

[JavaScriptObject]
public interface IHostBridge
{
}
""";

        var generated = RunGenerator<JavaScriptObjectSourceGenerator>(source, "IHostBridgeJavaScriptObjectMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Generator, Is.EqualTo("JavaScriptObject"));
            Assert.That(metadata.MetadataVersion, Is.EqualTo(1));
            Assert.That(metadata.IsGlobalExportEnabled, Is.False);
            Assert.That(metadata.Members.Length, Is.EqualTo(1));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("IHostBridge", "Interface", "IHostBridge")));
        });
    }

    [Test]
    public void ObjectStructGlobalMetadataReaderContractTest()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

[JavaScriptObject("hostStruct", IsGlobalExportEnabled = true)]
public readonly struct HostBridge
{
}
""";

        var generated = RunGenerator<JavaScriptObjectSourceGenerator>(source, "HostBridgeJavaScriptObjectMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Generator, Is.EqualTo("JavaScriptObject"));
            Assert.That(metadata.MetadataVersion, Is.EqualTo(1));
            Assert.That(metadata.IsGlobalExportEnabled, Is.True);
            Assert.That(metadata.Members.Length, Is.EqualTo(1));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("HostBridge", "Struct", "hostStruct")));
        });
    }

    [Test]
    public void FunctionMetadataReaderContractTest()
    {
        const string source = """
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

        var generated = RunGenerator<JavaScriptFunctionSourceGenerator>(source, "HostBridgeJavaScriptFunctionMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Generator, Is.EqualTo("JavaScriptFunction"));
            Assert.That(metadata.MetadataVersion, Is.EqualTo(1));
            Assert.That(metadata.Members.Length, Is.EqualTo(2));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("Sum", "Method", "sum", IsPure: true, IsInline: true)));
            Assert.That(metadata.Members[1], Is.EqualTo(new ReaderMemberContract("Multiply", "Method", "mul", IsPure: false, IsInline: false)));
        });
    }

    [Test]
    public void FunctionDefaultExportNameReaderContractTest()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptFunction]
    public int Invoke(int value) => value;
}
""";

        var generated = RunGenerator<JavaScriptFunctionSourceGenerator>(source, "HostBridgeJavaScriptFunctionMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.MetadataVersion, Is.EqualTo(1));
            Assert.That(metadata.Members.Length, Is.EqualTo(1));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("Invoke", "Method", "Invoke", IsPure: false, IsInline: true)));
        });
    }

    [Test]
    public void FunctionFullyQualifiedAttributeReaderContractTest()
    {
        const string source = """
namespace Demo;

public sealed class HostBridge
{
    [global::Atom.Compilers.JavaScript.JavaScriptFunction("sum", IsPure = true, IsInline = false)]
    public int Sum(int value) => value;
}
""";

        var generated = RunGenerator<JavaScriptFunctionSourceGenerator>(source, "HostBridgeJavaScriptFunctionMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.MetadataVersion, Is.EqualTo(1));
            Assert.That(metadata.Members.Length, Is.EqualTo(1));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("Sum", "Method", "sum", IsPure: true, IsInline: false)));
        });
    }

    [Test]
    public void FunctionOverloadAliasReaderContractTest()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptFunction("invokeInt")]
    public int Invoke(int value) => value;

    [JavaScriptFunction("invokeString")]
    public int Invoke(string value) => value.Length;
}
""";

        var generated = RunGenerator<JavaScriptFunctionSourceGenerator>(source, "HostBridgeJavaScriptFunctionMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Members.Length, Is.EqualTo(2));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("Invoke", "Method", "invokeInt", IsPure: false, IsInline: true)));
            Assert.That(metadata.Members[1], Is.EqualTo(new ReaderMemberContract("Invoke", "Method", "invokeString", IsPure: false, IsInline: true)));
        });
    }

    [Test]
    public void FunctionInterfaceInlineDisabledReaderContractTest()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public interface IHostBridge
{
    [JavaScriptFunction(IsInline = false)]
    int Invoke(int value);
}
""";

        var generated = RunGenerator<JavaScriptFunctionSourceGenerator>(source, "IHostBridgeJavaScriptFunctionMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Generator, Is.EqualTo("JavaScriptFunction"));
            Assert.That(metadata.MetadataVersion, Is.EqualTo(1));
            Assert.That(metadata.Members.Length, Is.EqualTo(1));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("Invoke", "Method", "Invoke", IsPure: false, IsInline: false)));
        });
    }

    [Test]
    public void DictionaryMetadataReaderContractTest()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

[JavaScriptDictionary(IsStringKeysOnly = false, IsPreserveEnumerationOrder = false)]
public sealed class HeaderMap
{
}
""";

        var generated = RunGenerator<JavaScriptDictionarySourceGenerator>(source, "HeaderMapJavaScriptDictionaryMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Generator, Is.EqualTo("JavaScriptDictionary"));
            Assert.That(metadata.MetadataVersion, Is.EqualTo(1));
            Assert.That(metadata.IsStringKeysOnly, Is.False);
            Assert.That(metadata.IsPreserveEnumerationOrder, Is.False);
            Assert.That(metadata.Members.Length, Is.EqualTo(1));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("HeaderMap", "Class", "HeaderMap")));
        });
    }

    [Test]
    public void DictionaryStructMetadataReaderContractTest()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

[JavaScriptDictionary]
public readonly struct HeaderMap
{
}
""";

        var generated = RunGenerator<JavaScriptDictionarySourceGenerator>(source, "HeaderMapJavaScriptDictionaryMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Generator, Is.EqualTo("JavaScriptDictionary"));
            Assert.That(metadata.MetadataVersion, Is.EqualTo(1));
            Assert.That(metadata.IsStringKeysOnly, Is.True);
            Assert.That(metadata.IsPreserveEnumerationOrder, Is.True);
            Assert.That(metadata.Members.Length, Is.EqualTo(1));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("HeaderMap", "Struct", "HeaderMap")));
        });
    }

    [Test]
    public void IgnoreMetadataReaderContractTest()
    {
        const string source = """
using System;
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptIgnore]
    private int left, right;

    [JavaScriptIgnore]
    public event Action? Changed;
}
""";

        var generated = RunGenerator<JavaScriptIgnoreSourceGenerator>(source, "HostBridgeJavaScriptIgnoreMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Generator, Is.EqualTo("JavaScriptIgnore"));
            Assert.That(metadata.MetadataVersion, Is.EqualTo(1));
            Assert.That(metadata.Members.Length, Is.EqualTo(3));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("left", "Field", null)));
            Assert.That(metadata.Members[1], Is.EqualTo(new ReaderMemberContract("right", "Field", null)));
            Assert.That(metadata.Members[2], Is.EqualTo(new ReaderMemberContract("Changed", "Event", null)));
        });
    }

    [Test]
    public void IgnoreIndexerEventReaderContractTest()
    {
        const string source = """
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

        var generated = RunGenerator<JavaScriptIgnoreSourceGenerator>(source, "HostBridgeJavaScriptIgnoreMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.MetadataVersion, Is.EqualTo(1));
            Assert.That(metadata.Members.Length, Is.EqualTo(2));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("Item", "Indexer", null)));
            Assert.That(metadata.Members[1], Is.EqualTo(new ReaderMemberContract("Changed", "Event", null)));
        });
    }

    [Test]
    public void IgnoreMultiEventReaderContractTest()
    {
        const string source = """
using System;
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptIgnore]
    public event Action? Changed, Updated;
}
""";

        var generated = RunGenerator<JavaScriptIgnoreSourceGenerator>(source, "HostBridgeJavaScriptIgnoreMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.MetadataVersion, Is.EqualTo(1));
            Assert.That(metadata.Members.Length, Is.EqualTo(2));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("Changed", "Event", null)));
            Assert.That(metadata.Members[1], Is.EqualTo(new ReaderMemberContract("Updated", "Event", null)));
        });
    }

    [Test]
    public void IgnorePartialAggregationReaderContractTest()
    {
        var generated = RunGenerator<JavaScriptIgnoreSourceGenerator>([
            """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed partial class HostBridge
{
    [JavaScriptIgnore]
    private int left, right;
}
""",
            """
using System;
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed partial class HostBridge
{
    [JavaScriptIgnore]
    public event Action? Changed;
}
"""
        ], "HostBridgeJavaScriptIgnoreMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.MetadataVersion, Is.EqualTo(1));
            Assert.That(metadata.Members.Length, Is.EqualTo(3));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("left", "Field", null)));
            Assert.That(metadata.Members[1], Is.EqualTo(new ReaderMemberContract("right", "Field", null)));
            Assert.That(metadata.Members[2], Is.EqualTo(new ReaderMemberContract("Changed", "Event", null)));
        });
    }

    [Test]
    public void IgnoreMetadataReaderMixedOrderingContractTest()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptIgnore]
    public int CachedValue { get; set; }

    [JavaScriptIgnore]
    private int left, right;

    [JavaScriptIgnore]
    public int Compute(int value) => value;
}
""";

        var generated = RunGenerator<JavaScriptIgnoreSourceGenerator>(source, "HostBridgeJavaScriptIgnoreMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Members.Length, Is.EqualTo(4));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("CachedValue", "Property", null)));
            Assert.That(metadata.Members[1], Is.EqualTo(new ReaderMemberContract("left", "Field", null)));
            Assert.That(metadata.Members[2], Is.EqualTo(new ReaderMemberContract("right", "Field", null)));
            Assert.That(metadata.Members[3], Is.EqualTo(new ReaderMemberContract("Compute", "Method", null)));
        });
    }

    [Test]
    public void IgnoreTypeMetadataReaderContractTest()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

[JavaScriptIgnore]
public sealed class HostBridge
{
}
""";

        var generated = RunGenerator<JavaScriptIgnoreSourceGenerator>(source, "HostBridgeJavaScriptIgnoreMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Generator, Is.EqualTo("JavaScriptIgnore"));
            Assert.That(metadata.MetadataVersion, Is.EqualTo(1));
            Assert.That(metadata.Members.Length, Is.EqualTo(1));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("HostBridge", "Class", null)));
        });
    }

    [Test]
    public void IgnoreInterfaceTypeMetadataReaderContractTest()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

[JavaScriptIgnore]
public interface IHostBridge
{
}
""";

        var generated = RunGenerator<JavaScriptIgnoreSourceGenerator>(source, "IHostBridgeJavaScriptIgnoreMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Generator, Is.EqualTo("JavaScriptIgnore"));
            Assert.That(metadata.MetadataVersion, Is.EqualTo(1));
            Assert.That(metadata.Members.Length, Is.EqualTo(1));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("IHostBridge", "Interface", null)));
        });
    }

    [Test]
    public void PropertyMetadataReaderContractTest()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty("count", IsRequired = true)]
    public int Count { get; set; }

    [JavaScriptProperty("name", IsReadOnly = true)]
    public string Name => "demo";
}
""";

        var generated = RunGenerator<JavaScriptPropertySourceGenerator>(source, "HostBridgeJavaScriptPropertyMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Generator, Is.EqualTo("JavaScriptProperty"));
            Assert.That(metadata.MetadataVersion, Is.EqualTo(1));
            Assert.That(metadata.Members.Length, Is.EqualTo(2));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("Count", "Property", "count", IsRequired: true)));
            Assert.That(metadata.Members[1], Is.EqualTo(new ReaderMemberContract("Name", "Property", "name", IsReadOnly: true)));
        });
    }

    [Test]
    public void PropertyDefaultExportNameReaderContractTest()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty]
    public int Count { get; set; }
}
""";

        var generated = RunGenerator<JavaScriptPropertySourceGenerator>(source, "HostBridgeJavaScriptPropertyMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.MetadataVersion, Is.EqualTo(1));
            Assert.That(metadata.Members.Length, Is.EqualTo(1));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("Count", "Property", "Count", IsReadOnly: false, IsRequired: false)));
        });
    }

    [Test]
    public void PropertyFullyQualifiedAttributeReaderContractTest()
    {
        const string source = """
namespace Demo;

public sealed class HostBridge
{
    [global::Atom.Compilers.JavaScript.JavaScriptProperty("count", IsRequired = true)]
    public int Count { get; set; }
}
""";

        var generated = RunGenerator<JavaScriptPropertySourceGenerator>(source, "HostBridgeJavaScriptPropertyMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.MetadataVersion, Is.EqualTo(1));
            Assert.That(metadata.Members.Length, Is.EqualTo(1));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("Count", "Property", "count", IsRequired: true)));
        });
    }

    [Test]
    public void PropertyRecordHostReaderContractTest()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed record HostBridge
{
    [JavaScriptProperty("count")]
    public int Count { get; init; }
}
""";

        var generated = RunGenerator<JavaScriptPropertySourceGenerator>(source, "HostBridgeJavaScriptPropertyMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Generator, Is.EqualTo("JavaScriptProperty"));
            Assert.That(metadata.MetadataVersion, Is.EqualTo(1));
            Assert.That(metadata.Members.Length, Is.EqualTo(1));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("Count", "Property", "count", IsReadOnly: false, IsRequired: false)));
        });
    }

    [Test]
    public void PropertyMetadataReaderOrderingAcrossPartialsTest()
    {
        var generated = RunGenerator<JavaScriptPropertySourceGenerator>([
            """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed partial class HostBridge
{
    [JavaScriptProperty(\"count\")]
    public int Count { get; set; }
}
""",
            """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed partial class HostBridge
{
    [JavaScriptProperty(\"total\")]
    public int Total { get; set; }
}
"""
        ], "HostBridgeJavaScriptPropertyMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Members.Length, Is.EqualTo(2));
            Assert.That(metadata.Members[0].Name, Is.EqualTo("Count"));
            Assert.That(metadata.Members[1].Name, Is.EqualTo("Total"));
        });
    }

    [Test]
    public void FunctionMetadataReaderOrderingAcrossPartialsTest()
    {
        var generated = RunGenerator<JavaScriptFunctionSourceGenerator>([
            """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed partial class HostBridge
{
    [JavaScriptFunction("sum", IsPure = true)]
    public int Sum(int value) => value;
}
""",
            """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed partial class HostBridge
{
    [JavaScriptFunction("mul", IsInline = false)]
    public int Multiply(int value) => value;
}
"""
        ], "HostBridgeJavaScriptFunctionMetadata.g.cs");
        var metadata = ParseContract(generated);

        Assert.Multiple(() =>
        {
            Assert.That(metadata.Members.Length, Is.EqualTo(2));
            Assert.That(metadata.Members[0], Is.EqualTo(new ReaderMemberContract("Sum", "Method", "sum", IsPure: true, IsInline: true)));
            Assert.That(metadata.Members[1], Is.EqualTo(new ReaderMemberContract("Multiply", "Method", "mul", IsPure: false, IsInline: false)));
        });
    }

    private static string RunGenerator<TGenerator>(string source, string hintName)
        where TGenerator : IIncrementalGenerator, new()
        => RunGenerator<TGenerator>([source], hintName);

    private static string RunGenerator<TGenerator>(IReadOnlyList<string> sources, string hintName)
        where TGenerator : IIncrementalGenerator, new()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var syntaxTrees = sources.Select(source => CSharpSyntaxTree.ParseText(source, parseOptions)).ToArray();
        var compilation = CSharpCompilation.Create(
            assemblyName: typeof(TGenerator).Name,
            syntaxTrees: syntaxTrees,
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new TGenerator()).WithUpdatedParseOptions(parseOptions);
        driver = driver.RunGenerators(compilation);

        var runResult = driver.GetRunResult();
        var generatedSource = runResult.Results
            .SelectMany(static result => result.GeneratedSources)
            .Single(generated => string.Equals(generated.HintName, hintName, StringComparison.Ordinal));

        return generatedSource.SourceText.ToString();
    }

    private static ImmutableArray<MetadataReference> GetMetadataReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        Assert.That(trustedPlatformAssemblies, Is.Not.Null.And.Not.Empty);

        var references = ImmutableArray.CreateBuilder<MetadataReference>();

        foreach (var path in trustedPlatformAssemblies!.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            references.Add(MetadataReference.CreateFromFile(path));

        references.Add(JavaScriptTestReferences.Atom);
        references.Add(JavaScriptTestReferences.Runtime);

        return references.ToImmutable();
    }

    private static ReaderTypeContract ParseContract(string generatedSource)
    {
        var constants = ConstantRegex()
            .Matches(generatedSource)
            .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["value"].Value, StringComparer.Ordinal);

        var memberCount = int.Parse(constants["AnnotatedMemberCount"], global::System.Globalization.CultureInfo.InvariantCulture);
        var members = ImmutableArray.CreateBuilder<ReaderMemberContract>(memberCount);

        for (var i = 0; i < memberCount; i++)
        {
            members.Add(new ReaderMemberContract(
                Name: Unquote(constants[$"Member{i}Name"]),
                Kind: Unquote(constants[$"Member{i}Kind"]),
                ExportName: TryGetString(constants, $"Member{i}ExportName"),
                IsReadOnly: TryGetBoolean(constants, $"Member{i}IsReadOnly"),
                IsRequired: TryGetBoolean(constants, $"Member{i}IsRequired"),
                IsPure: TryGetBoolean(constants, $"Member{i}IsPure"),
                IsInline: TryGetBoolean(constants, $"Member{i}IsInline")));
        }

        return new ReaderTypeContract(
            EntityName: Unquote(constants["EntityName"]),
            Generator: Unquote(constants["Generator"]),
            MetadataVersion: int.Parse(constants["MetadataVersion"], global::System.Globalization.CultureInfo.InvariantCulture),
            Members: members.MoveToImmutable(),
            IsGlobalExportEnabled: TryGetBoolean(constants, "IsGlobalExportEnabled"),
            IsStringKeysOnly: TryGetBoolean(constants, "IsStringKeysOnly", defaultValue: true),
            IsPreserveEnumerationOrder: TryGetBoolean(constants, "IsPreserveEnumerationOrder", defaultValue: true));
    }

    private static string? TryGetString(Dictionary<string, string> constants, string key)
        => constants.TryGetValue(key, out var value) ? Unquote(value) : null;

    private static bool TryGetBoolean(Dictionary<string, string> constants, string key, bool defaultValue = false)
        => constants.TryGetValue(key, out var value) ? bool.Parse(value) : defaultValue;

    private static string Unquote(string value)
        => value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    private sealed record ReaderTypeContract(
        string EntityName,
        string Generator,
        int MetadataVersion,
        ImmutableArray<ReaderMemberContract> Members,
        bool IsGlobalExportEnabled,
        bool IsStringKeysOnly,
        bool IsPreserveEnumerationOrder);

    private sealed record ReaderMemberContract(
        string Name,
        string Kind,
        string? ExportName,
        bool IsReadOnly = false,
        bool IsRequired = false,
        bool IsPure = false,
        bool IsInline = false);

    [GeneratedRegex("^\\s*internal const (?<type>\\w+) (?<name>\\w+) = (?<value>.+);$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ConstantRegex();
}