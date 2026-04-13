using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Atom.Compilers.JavaScript.SourceGeneration.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class JavaScriptValidationAnalyzerTests
{
    [Test]
    public async Task TypeConflictAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

[JavaScriptObject]
[JavaScriptDictionary]
public sealed class HostBridge
{
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptTypeAttributeConflictAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS100", DiagnosticSeverity.Error)
            .WithSpan(7, 21, 7, 31)
            .WithMessage("Тип 'HostBridge' не может одновременно использовать атрибуты 'JavaScriptObject' и 'JavaScriptDictionary'"));

        await test.RunAsync();
    }

    [Test]
    public async Task GenericFunctionAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptFunction]
    public T Call<T>(T value) => value;
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptFunctionGenericMethodAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS101", DiagnosticSeverity.Error)
            .WithSpan(8, 14, 8, 18)
            .WithMessage("Метод 'Call' с атрибутом 'JavaScriptFunction' не должен быть generic на текущем этапе runtime scaffolding"));

        await test.RunAsync();
    }

    [Test]
    public async Task StaticPropertyAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty]
    public static int Count { get; set; }
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptPropertyStaticMemberAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS102", DiagnosticSeverity.Error)
            .WithSpan(8, 23, 8, 28)
            .WithMessage("Static member 'Count' с атрибутом 'JavaScriptProperty' пока не поддерживается генератором host bindings"));

        await test.RunAsync();
    }

    [Test]
    public async Task DuplicateFunctionExportNameAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptFunction]
    public int Call(int value) => value;

    [JavaScriptFunction]
    public int Call(string value) => value.Length;
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptDuplicateExportNameAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS103", DiagnosticSeverity.Error)
            .WithSpan(8, 16, 8, 20)
            .WithMessage("Exported JavaScript name 'Call' дублируется внутри типа 'HostBridge'"));

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS103", DiagnosticSeverity.Error)
            .WithSpan(11, 16, 11, 20)
            .WithMessage("Exported JavaScript name 'Call' дублируется внутри типа 'HostBridge'"));

        await test.RunAsync();
    }

    [Test]
    public async Task DuplicatePropertyAliasAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty("Count")]
    public int Value { get; set; }

    [JavaScriptProperty]
    public int Count { get; set; }
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptDuplicateExportNameAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS103", DiagnosticSeverity.Error)
            .WithSpan(8, 16, 8, 21)
            .WithMessage("Exported JavaScript name 'Count' дублируется внутри типа 'HostBridge'"));

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS103", DiagnosticSeverity.Error)
            .WithSpan(11, 16, 11, 21)
            .WithMessage("Exported JavaScript name 'Count' дублируется внутри типа 'HostBridge'"));

        await test.RunAsync();
    }

    [Test]
    public async Task MixedPropertyAndFunctionDuplicateExportAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty("Invoke")]
    public int Value { get; set; }

    [JavaScriptFunction]
    public int Invoke(int value) => value;
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptDuplicateExportNameAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS103", DiagnosticSeverity.Error)
            .WithSpan(8, 16, 8, 21)
            .WithMessage("Exported JavaScript name 'Invoke' дублируется внутри типа 'HostBridge'"));

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS103", DiagnosticSeverity.Error)
            .WithSpan(11, 16, 11, 22)
            .WithMessage("Exported JavaScript name 'Invoke' дублируется внутри типа 'HostBridge'"));

        await test.RunAsync();
    }

    [Test]
    public async Task ExplicitInterfaceFunctionDuplicateExportAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public interface IHostBridge
{
    int Invoke(int value);
}

public sealed class HostBridge : IHostBridge
{
    [JavaScriptFunction]
    public int Invoke(int value) => value;

    [JavaScriptFunction("Invoke")]
    int IHostBridge.Invoke(int value) => value;
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptDuplicateExportNameAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS103", DiagnosticSeverity.Error)
            .WithSpan(13, 16, 13, 22)
            .WithMessage("Exported JavaScript name 'Invoke' дублируется внутри типа 'HostBridge'"));

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS103", DiagnosticSeverity.Error)
            .WithSpan(16, 21, 16, 27)
            .WithMessage("Exported JavaScript name 'Invoke' дублируется внутри типа 'HostBridge'"));

        await test.RunAsync();
    }

    [Test]
    public async Task FieldJavaScriptPropertyUnsupportedAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty]
    private int count;
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptPropertyFieldUnsupportedAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS104", DiagnosticSeverity.Error)
            .WithSpan(8, 17, 8, 22)
            .WithMessage("Field 'count' с атрибутом 'JavaScriptProperty' пока не поддерживается текущим generator scaffold; используйте property"));

        await test.RunAsync();
    }

    [Test]
    public async Task MultiFieldJavaScriptPropertyUnsupportedAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty]
    private int left, right;
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptPropertyFieldUnsupportedAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS104", DiagnosticSeverity.Error)
            .WithSpan(8, 17, 8, 21)
            .WithMessage("Field 'left' с атрибутом 'JavaScriptProperty' пока не поддерживается текущим generator scaffold; используйте property"));

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS104", DiagnosticSeverity.Error)
            .WithSpan(8, 23, 8, 28)
            .WithMessage("Field 'right' с атрибутом 'JavaScriptProperty' пока не поддерживается текущим generator scaffold; используйте property"));

        await test.RunAsync();
    }

    [Test]
    public async Task EmptyPropertyAliasAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty("")]
    public int Value { get; set; }
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptEmptyExportNameAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS105", DiagnosticSeverity.Error)
            .WithSpan(7, 25, 7, 27)
            .WithMessage("Explicit JavaScript export name для 'JavaScriptProperty' не должен быть пустым"));

        await test.RunAsync();
    }

    [Test]
    public async Task EmptyFunctionAliasAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptFunction("")]
    public int Invoke(int value) => value;
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptEmptyExportNameAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS105", DiagnosticSeverity.Error)
            .WithSpan(7, 25, 7, 27)
            .WithMessage("Explicit JavaScript export name для 'JavaScriptFunction' не должен быть пустым"));

        await test.RunAsync();
    }

    [Test]
    public async Task EmptyObjectAliasAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

[JavaScriptObject("")]
public sealed class HostBridge
{
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptEmptyExportNameAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS105", DiagnosticSeverity.Error)
            .WithSpan(5, 19, 5, 21)
            .WithMessage("Explicit JavaScript export name для 'JavaScriptObject' не должен быть пустым"));

        await test.RunAsync();
    }

    [Test]
    public async Task InterfaceTypeConflictAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

[JavaScriptObject]
[JavaScriptDictionary]
public interface IHostBridge
{
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptTypeAttributeConflictAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS100", DiagnosticSeverity.Error)
            .WithSpan(7, 18, 7, 29)
            .WithMessage("Тип 'IHostBridge' не может одновременно использовать атрибуты 'JavaScriptObject' и 'JavaScriptDictionary'"));

        await test.RunAsync();
    }

    [Test]
    public async Task IgnoredMemberIsExcludedFromDuplicateExportAnalysisAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty("Invoke")]
    [JavaScriptIgnore]
    public int Value { get; set; }

    [JavaScriptFunction]
    public int Invoke(int value) => value;
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptDuplicateExportNameAnalyzer, DefaultVerifier>
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

        await test.RunAsync();
    }

    [Test]
    public async Task IgnoredGenericFunctionSkipsValidationAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptFunction]
    [JavaScriptIgnore]
    public T Call<T>(T value) => value;
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptFunctionGenericMethodAnalyzer, DefaultVerifier>
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

        await test.RunAsync();
    }

    [Test]
    public async Task IgnoredTypeSkipsConflictValidationAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

[JavaScriptObject]
[JavaScriptDictionary]
[JavaScriptIgnore]
public sealed class HostBridge
{
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptTypeAttributeConflictAnalyzer, DefaultVerifier>
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

        await test.RunAsync();
    }

    [Test]
    public async Task IgnoredFieldSkipsUnsupportedFieldValidationAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty]
    [JavaScriptIgnore]
    private int count;
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptPropertyFieldUnsupportedAnalyzer, DefaultVerifier>
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

        await test.RunAsync();
    }

    [Test]
    public async Task IgnoredStaticPropertySkipsValidationAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty]
    [JavaScriptIgnore]
    public static int Count { get; set; }
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptPropertyStaticMemberAnalyzer, DefaultVerifier>
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

        await test.RunAsync();
    }

    [Test]
    public async Task IgnoredEmptyPropertyAliasSkipsValidationAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty("")]
    [JavaScriptIgnore]
    public int Value { get; set; }
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptEmptyExportNameAnalyzer, DefaultVerifier>
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

        await test.RunAsync();
    }

    [Test]
    public async Task IgnoredFieldDoesNotCollideWithPropertyAliasAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty("count")]
    [JavaScriptIgnore]
    private int cachedCount;

    [JavaScriptProperty]
    public int count { get; set; }
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptDuplicateExportNameAnalyzer, DefaultVerifier>
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

        await test.RunAsync();
    }

    [Test]
    public async Task PartialTypeConflictAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

[JavaScriptObject]
public sealed partial class HostBridge
{
}

[JavaScriptDictionary]
public sealed partial class HostBridge
{
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptTypeAttributeConflictAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS100", DiagnosticSeverity.Error)
            .WithSpan(6, 29, 6, 39)
            .WithMessage("Тип 'HostBridge' не может одновременно использовать атрибуты 'JavaScriptObject' и 'JavaScriptDictionary'"));

        await test.RunAsync();
    }

    [Test]
    public async Task PartialDuplicateExportAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed partial class HostBridge
{
    [JavaScriptProperty("call")]
    public int Value { get; set; }
}

public sealed partial class HostBridge
{
    [JavaScriptFunction("call")]
    public int Invoke(int value) => value;
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptDuplicateExportNameAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS103", DiagnosticSeverity.Error)
            .WithSpan(8, 16, 8, 21)
            .WithMessage("Exported JavaScript name 'call' дублируется внутри типа 'HostBridge'"));

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS103", DiagnosticSeverity.Error)
            .WithSpan(14, 16, 14, 22)
            .WithMessage("Exported JavaScript name 'call' дублируется внутри типа 'HostBridge'"));

        await test.RunAsync();
    }

    [Test]
    public async Task IndexerJavaScriptPropertyUnsupportedAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty]
    public int this[int index]
    {
        get => index;
        set { }
    }
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptPropertyIndexerUnsupportedAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS106", DiagnosticSeverity.Error)
            .WithSpan(8, 16, 8, 20)
            .WithMessage("Indexer с атрибутом 'JavaScriptProperty' пока не поддерживается текущим generator scaffold; используйте именованное property-обёртывание"));

        await test.RunAsync();
    }

    [Test]
    public async Task IgnoredIndexerSkipsUnsupportedPropertyAnalyzerAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty]
    [JavaScriptIgnore]
    public int this[int index]
    {
        get => index;
        set { }
    }
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptPropertyIndexerUnsupportedAnalyzer, DefaultVerifier>
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

        await test.RunAsync();
    }

    [Test]
    public async Task ReadOnlyPropertyWithoutGetterAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty(IsReadOnly = true)]
    public int Value
    {
        set { }
    }
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptPropertyReadOnlyWithoutGetterAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS107", DiagnosticSeverity.Error)
            .WithSpan(8, 16, 8, 21)
            .WithMessage("Member 'Value' использует 'JavaScriptProperty(IsReadOnly = true)', но не содержит getter"));

        await test.RunAsync();
    }

    [Test]
    public async Task IgnoredReadOnlyPropertyWithoutGetterSkipsAnalyzerAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty(IsReadOnly = true)]
    [JavaScriptIgnore]
    public int Value
    {
        set { }
    }
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptPropertyReadOnlyWithoutGetterAnalyzer, DefaultVerifier>
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

        await test.RunAsync();
    }

    [Test]
    public async Task InterfaceFunctionInlineUnsupportedAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public interface IHostBridge
{
    [JavaScriptFunction]
    int Invoke(int value);
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptFunctionInlineUnsupportedAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS108", DiagnosticSeverity.Error)
            .WithSpan(8, 9, 8, 15)
            .WithMessage("Member 'Invoke' использует 'JavaScriptFunction(IsInline = true)', но abstract/interface methods не имеют inline body для текущего adapter scaffold"));

        await test.RunAsync();
    }

    [Test]
    public async Task AbstractFunctionWithInlineDisabledSkipsAnalyzerAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public abstract class HostBridge
{
    [JavaScriptFunction(IsInline = false)]
    public abstract int Invoke(int value);
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptFunctionInlineUnsupportedAnalyzer, DefaultVerifier>
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

        await test.RunAsync();
    }

    [Test]
    public async Task RequiredPropertyWithoutGetterAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptProperty(IsRequired = true)]
    public int Value
    {
        set { }
    }
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptPropertyRequiredWithoutGetterAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS109", DiagnosticSeverity.Error)
            .WithSpan(8, 16, 8, 21)
            .WithMessage("Member 'Value' использует 'JavaScriptProperty(IsRequired = true)', но не содержит getter"));

        await test.RunAsync();
    }

    [Test]
    public async Task PureAsyncFunctionAnalyzerTestAsync()
    {
        const string source = """
using System.Threading.Tasks;
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptFunction(IsPure = true)]
    public async Task<int> InvokeAsync(int value)
    {
        await Task.Yield();
        return value;
    }
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptFunctionPureAsyncAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS110", DiagnosticSeverity.Error)
            .WithSpan(9, 28, 9, 39)
            .WithMessage("Member 'InvokeAsync' использует 'JavaScriptFunction(IsPure = true)', но async methods не поддерживаются как pure-path в текущем scaffold"));

        await test.RunAsync();
    }

    [Test]
    public async Task PureIteratorFunctionAnalyzerTestAsync()
    {
        const string source = """
using System.Collections.Generic;
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptFunction(IsPure = true)]
    public IEnumerable<int> Enumerate(int value)
    {
        yield return value;
    }
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptFunctionPureIteratorAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS111", DiagnosticSeverity.Error)
            .WithSpan(9, 29, 9, 38)
            .WithMessage("Member 'Enumerate' использует 'JavaScriptFunction(IsPure = true)', но iterator methods с 'yield' не поддерживаются как pure-path в текущем scaffold"));

        await test.RunAsync();
    }

    [Test]
    public async Task PureAbstractFunctionAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public abstract class HostBridge
{
    [JavaScriptFunction(IsPure = true)]
    public abstract int Invoke(int value);
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptFunctionPureAbstractAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS113", DiagnosticSeverity.Error)
            .WithSpan(8, 25, 8, 31)
            .WithMessage("Member 'Invoke' использует 'JavaScriptFunction(IsPure = true)', но abstract/interface methods не имеют compile-time body для pure-path validation в текущем scaffold"));

        await test.RunAsync();
    }

    [Test]
    public async Task PureVoidFunctionAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptFunction(IsPure = true)]
    public void Invoke(int value)
    {
    }
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptFunctionPureVoidAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS114", DiagnosticSeverity.Error)
            .WithSpan(8, 17, 8, 23)
            .WithMessage("Member 'Invoke' использует 'JavaScriptFunction(IsPure = true)', но void-return methods не формируют value-producing pure contract в текущем scaffold"));

        await test.RunAsync();
    }

    [Test]
    public async Task PureExternFunctionAnalyzerTestAsync()
    {
        const string source = """
using System.Runtime.InteropServices;
using Atom.Compilers.JavaScript;

namespace Demo;

public static class NativeBridge
{
    [JavaScriptFunction(IsPure = true)]
    [DllImport("native")]
    public static extern int Invoke(int value);
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptFunctionPureBodylessAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS115", DiagnosticSeverity.Error)
            .WithSpan(10, 30, 10, 36)
            .WithMessage("Member 'Invoke' использует 'JavaScriptFunction(IsPure = true)', но не имеет compile-time body для pure-path validation в текущем scaffold"));

        await test.RunAsync();
    }

    [Test]
    public async Task PureTaskLikeFunctionAnalyzerTestAsync()
    {
        const string source = """
using System.Threading.Tasks;
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptFunction(IsPure = true)]
    public Task<int> Invoke(int value) => Task.FromResult(value);
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptFunctionPureTaskLikeAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS116", DiagnosticSeverity.Error)
            .WithSpan(9, 22, 9, 28)
            .WithMessage("Member 'Invoke' использует 'JavaScriptFunction(IsPure = true)', но task-like return types не поддерживаются как synchronous pure contract в текущем scaffold"));

        await test.RunAsync();
    }

    [Test]
    public async Task PureFunctionByRefAnalyzerTestAsync()
    {
        const string source = """
using Atom.Compilers.JavaScript;

namespace Demo;

public sealed class HostBridge
{
    [JavaScriptFunction(IsPure = true)]
    public int Invoke(ref int value) => value;
}
""";

        var test = new CSharpAnalyzerTest<JavaScriptFunctionPureByRefAnalyzer, DefaultVerifier>
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

        test.ExpectedDiagnostics.Add(new DiagnosticResult("ATOMJS112", DiagnosticSeverity.Error)
            .WithSpan(8, 16, 8, 22)
            .WithMessage("Member 'Invoke' использует 'JavaScriptFunction(IsPure = true)', но ref/out/in parameters не поддерживаются как pure-path в текущем scaffold"));

        await test.RunAsync();
    }
}