using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptFunctionPureBodylessAnalyzerSyntaxProvider : AnalyzerSyntaxProvider
{
    public static DiagnosticDescriptor RuleDescriptor { get; } = new(
        "ATOMJS115",
        "Pure JavaScriptFunction недопустим для body-less methods",
        "Member '{0}' использует 'JavaScriptFunction(IsPure = true)', но не имеет compile-time body для pure-path validation в текущем scaffold",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Текущий pure-path generator scaffolding требует доступное method body или expression body для compile-time validation."
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
        if (JavaScriptFunctionPureAnalyzerSyntaxHelpers.IsAbstractOrInterfaceMethod(node)) return;
        if (node.Body is not null || node.ExpressionBody is not null) return;

        context.ReportDiagnostic(Diagnostic.Create(RuleDescriptor, node.Identifier.GetLocation(), node.Identifier.Text));
    }
}