using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptFunctionPureTaskLikeAnalyzerSyntaxProvider : AnalyzerSyntaxProvider
{
    public static DiagnosticDescriptor RuleDescriptor { get; } = new(
        "ATOMJS116",
        "Pure JavaScriptFunction недопустим для task-like return types",
        "Member '{0}' использует 'JavaScriptFunction(IsPure = true)', но task-like return types не поддерживаются как synchronous pure contract в текущем scaffold",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Текущий pure-path generator scaffolding трактует pure export как синхронный value-producing contract и не поддерживает Task/ValueTask return types."
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

        if (context.SemanticModel.GetTypeInfo(node.ReturnType, context.CancellationToken).Type is not INamedTypeSymbol returnType) return;
        if (!IsTaskLike(returnType)) return;

        context.ReportDiagnostic(Diagnostic.Create(RuleDescriptor, node.Identifier.GetLocation(), node.Identifier.Text));
    }

    private static bool IsTaskLike(INamedTypeSymbol returnType)
    {
        if (!string.Equals(returnType.ContainingNamespace.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal))
            return false;

        return string.Equals(returnType.Name, "Task", StringComparison.Ordinal)
            || string.Equals(returnType.Name, "ValueTask", StringComparison.Ordinal);
    }
}