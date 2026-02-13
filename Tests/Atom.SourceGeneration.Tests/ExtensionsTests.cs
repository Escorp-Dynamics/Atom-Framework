using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Atom.SourceGeneration.Tests;

/// <summary>
/// Тесты для методов расширения <see cref="Extensions"/>.
/// </summary>
[Parallelizable(ParallelScope.All)]
public class ExtensionsTests(ILogger logger) : BenchmarkTests<ExtensionsTests>(logger)
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ExtensionsTests"/>.
    /// </summary>
    public ExtensionsTests() : this(ConsoleLogger.Unicode) { }

    /// <summary>
    /// Тест получения параметра атрибута по типу.
    /// </summary>
    [TestCase(TestName = "Тест GetParameter с типом")]
    public void GetParameterByTypeTest()
    {
        var source = """
            using System;

            [AttributeUsage(AttributeTargets.Class)]
            public class TestAttribute : Attribute
            {
                public string? Name { get; set; }
                public int Value { get; set; }
                public bool Flag { get; set; }
            }

            [Test(Name = "TestName", Value = 42, Flag = true)]
            public class TestClass { }
            """;

        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var classDecl = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
            .First(c => c.Identifier.Text == "TestClass");

        var symbol = model.GetDeclaredSymbol(classDecl);
        var attribute = symbol?.GetAttributes().FirstOrDefault();

        if (attribute is not null)
        {
            var stringParam = attribute.GetParameter<string>("Name");
            var intParam = attribute.GetParameter<int>("Value");
            var boolParam = attribute.GetParameter<bool>("Flag");

            Assert.That(stringParam, Is.EqualTo("TestName"));
            Assert.That(intParam, Is.EqualTo(42));
            Assert.That(boolParam, Is.True);
        }
    }

    /// <summary>
    /// Тест парсинга XML-документации.
    /// </summary>
    [TestCase(TestName = "Тест TryParseXmlDocumentation")]
    public void TryParseXmlDocumentationTest()
    {
        var source = """
            /// <summary>
            /// Тестовый класс для проверки парсинга документации.
            /// </summary>
            /// <remarks>
            /// Дополнительные замечания.
            /// </remarks>
            public class DocumentedClass
            {
                /// <summary>
                /// Метод с параметрами.
                /// </summary>
                /// <param name="arg1">Первый аргумент.</param>
                /// <param name="arg2">Второй аргумент.</param>
                /// <typeparam name="T">Тип элемента.</typeparam>
                /// <returns>Результат операции.</returns>
                public T Method<T>(string arg1, int arg2) => default;
            }
            """;

        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);

        // Тест для класса
        var classDecl = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
            .First();

        var classSymbol = model.GetDeclaredSymbol(classDecl);
        Assert.That(classSymbol, Is.Not.Null);
        classSymbol!.TryParseXmlDocumentation(
            out var classSummary,
            out _,
            out _,
            out _,
            out _,
            out var classRemarks);

        Assert.That(classSummary, Does.Contain("Тестовый класс"));
        Assert.That(classRemarks, Does.Contain("Дополнительные замечания"));

        // Тест для метода
        var methodDecl = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .First();

        var methodSymbol = model.GetDeclaredSymbol(methodDecl);
        Assert.That(methodSymbol, Is.Not.Null);
        methodSymbol!.TryParseXmlDocumentation(
            out var methodSummary,
            out var paramComments,
            out var typeparamComments,
            out var returnsComment,
            out _,
            out _);

        Assert.That(methodSummary, Does.Contain("Метод с параметрами"));
        Assert.That(paramComments, Is.Not.Null);
        Assert.That(paramComments!.ContainsKey("arg1"), Is.True);
        Assert.That(paramComments["arg1"], Does.Contain("Первый аргумент"));
        Assert.That(typeparamComments, Is.Not.Null);
        Assert.That(typeparamComments!.ContainsKey("T"), Is.True);
        Assert.That(returnsComment, Does.Contain("Результат операции"));
    }

    /// <summary>
    /// Тест упрощённого парсинга документации.
    /// </summary>
    [TestCase(TestName = "Тест TryParseXmlDocumentation (только summary)")]
    public void TryParseXmlDocumentationSimpleTest()
    {
        var source = """
            /// <summary>
            /// Простой класс.
            /// </summary>
            public class SimpleClass { }
            """;

        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var classDecl = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
            .First();

        var symbol = model.GetDeclaredSymbol(classDecl);
        Assert.That(symbol, Is.Not.Null);
        symbol!.TryParseXmlDocumentation(out var summary);

        Assert.That(summary, Does.Contain("Простой класс"));
    }

    /// <summary>
    /// Тест парсинга документации без комментария.
    /// </summary>
    [TestCase(TestName = "Тест TryParseXmlDocumentation без документации")]
    public void TryParseXmlDocumentationEmptyTest()
    {
        var source = "public class UndocumentedClass { }";

        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);
        var classDecl = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
            .First();

        var symbol = model.GetDeclaredSymbol(classDecl);
        Assert.That(symbol, Is.Not.Null);
        symbol!.TryParseXmlDocumentation(out var summary);

        Assert.That(summary, Is.Null);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
