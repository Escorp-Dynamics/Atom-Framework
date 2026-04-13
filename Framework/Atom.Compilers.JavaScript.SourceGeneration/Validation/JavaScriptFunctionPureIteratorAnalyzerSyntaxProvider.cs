using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptFunctionPureIteratorAnalyzerSyntaxProvider : AnalyzerSyntaxProvider
{
    public static DiagnosticDescriptor RuleDescriptor { get; } = new(
        "ATOMJS111",
        "Pure JavaScriptFunction недопустим для iterator methods",
        "Member '{0}' использует 'JavaScriptFunction(IsPure = true)', но iterator methods с 'yield' не поддерживаются как pure-path в текущем scaffold",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Текущая pure-модель generator scaffolding не трактует iterator methods как допустимый pure fast-path."
    );

    public override string Id => RuleDescriptor.Id;

    public override ImmutableArray<SyntaxKind> SyntaxKinds => [SyntaxKind.MethodDeclaration];

    public override DiagnosticDescriptor Rule => RuleDescriptor;

    public override void Execute(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MethodDeclarationSyntax node) return;
        if (JavaScriptAnalyzerSyntaxHelpers.IsIgnored(node.AttributeLists)) return;
        if (!JavaScriptAnalyzerSyntaxHelpers.HasAttribute(node.AttributeLists, JavaScriptAttributeNames.Function)) return;
        if (!JavaScriptFunctionPureAnalyzerSyntaxHelpers.HasPureFlag(node.AttributeLists)) return;
        if (!node.DescendantNodes().Any(static descendant => descendant.IsKind(SyntaxKind.YieldReturnStatement) || descendant.IsKind(SyntaxKind.YieldBreakStatement))) return;

        context.ReportDiagnostic(Diagnostic.Create(RuleDescriptor, node.Identifier.GetLocation(), node.Identifier.Text));
    }
}