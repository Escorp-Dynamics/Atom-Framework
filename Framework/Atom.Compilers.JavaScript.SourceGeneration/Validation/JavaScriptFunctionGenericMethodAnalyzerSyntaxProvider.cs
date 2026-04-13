using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptFunctionGenericMethodAnalyzerSyntaxProvider : AnalyzerSyntaxProvider
{
    public static DiagnosticDescriptor RuleDescriptor { get; } = new(
        "ATOMJS101",
        "Неподдерживаемая generic JavaScriptFunction",
        "Метод '{0}' с атрибутом 'JavaScriptFunction' не должен быть generic на текущем этапе runtime scaffolding",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Generic методы пока не имеют стабильной export-модели для JavaScript host binding."
    );

    public override string Id => RuleDescriptor.Id;

    public override ImmutableArray<SyntaxKind> SyntaxKinds => [SyntaxKind.MethodDeclaration];

    public override DiagnosticDescriptor Rule => RuleDescriptor;

    public override void Execute(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MethodDeclarationSyntax node) return;
        if (JavaScriptAnalyzerSyntaxHelpers.IsIgnored(node.AttributeLists)) return;
        if (!JavaScriptAnalyzerSyntaxHelpers.HasAttribute(node.AttributeLists, JavaScriptAttributeNames.Function)) return;
        if (node.TypeParameterList is null || node.TypeParameterList.Parameters.Count is 0) return;

        context.ReportDiagnostic(Diagnostic.Create(RuleDescriptor, node.Identifier.GetLocation(), node.Identifier.Text));
    }
}