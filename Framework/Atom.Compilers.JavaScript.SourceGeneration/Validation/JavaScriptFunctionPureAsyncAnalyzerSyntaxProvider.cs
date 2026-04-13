using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptFunctionPureAsyncAnalyzerSyntaxProvider : AnalyzerSyntaxProvider
{
    public static DiagnosticDescriptor RuleDescriptor { get; } = new(
        "ATOMJS110",
        "Pure JavaScriptFunction недопустим для async methods",
        "Member '{0}' использует 'JavaScriptFunction(IsPure = true)', но async methods не поддерживаются как pure-path в текущем scaffold",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Текущая pure-модель generator scaffolding не трактует async methods как допустимый pure fast-path."
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
        if (!node.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.AsyncKeyword))) return;

        context.ReportDiagnostic(Diagnostic.Create(RuleDescriptor, node.Identifier.GetLocation(), node.Identifier.Text));
    }
}