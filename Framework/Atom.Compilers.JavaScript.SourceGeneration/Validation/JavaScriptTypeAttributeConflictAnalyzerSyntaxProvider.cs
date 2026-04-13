using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptTypeAttributeConflictAnalyzerSyntaxProvider : AnalyzerSyntaxProvider
{
    public static DiagnosticDescriptor RuleDescriptor { get; } = new(
        "ATOMJS100",
        "Конфликт JavaScript type-атрибутов",
        "Тип '{0}' не может одновременно использовать атрибуты 'JavaScriptObject' и 'JavaScriptDictionary'",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "JavaScript type export должен выбирать одну модель shape-generation на тип."
    );

    public override string Id => RuleDescriptor.Id;

    public override ImmutableArray<SyntaxKind> SyntaxKinds =>
    [
        SyntaxKind.ClassDeclaration,
        SyntaxKind.StructDeclaration,
        SyntaxKind.InterfaceDeclaration,
    ];

    public override DiagnosticDescriptor Rule => RuleDescriptor;

    public override void Execute(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not TypeDeclarationSyntax node) return;
        if (context.SemanticModel.GetDeclaredSymbol(node) is not INamedTypeSymbol symbol) return;
        if (!IsPrimaryDeclaration(symbol, node)) return;
        if (JavaScriptAnalyzerSyntaxHelpers.IsIgnored(symbol)) return;

        var hasObject = JavaScriptAnalyzerSyntaxHelpers.HasAttribute(symbol, JavaScriptAttributeNames.Object);
        var hasDictionary = JavaScriptAnalyzerSyntaxHelpers.HasAttribute(symbol, JavaScriptAttributeNames.Dictionary);

        if (!hasObject || !hasDictionary) return;

        context.ReportDiagnostic(Diagnostic.Create(RuleDescriptor, node.Identifier.GetLocation(), node.Identifier.Text));
    }

    private static bool IsPrimaryDeclaration(INamedTypeSymbol symbol, TypeDeclarationSyntax node)
        => symbol.DeclaringSyntaxReferences.Select(static reference => reference.Span.Start).Min() == node.SpanStart;
}