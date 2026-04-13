using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptFunctionPureByRefAnalyzerSyntaxProvider : AnalyzerSyntaxProvider
{
    public static DiagnosticDescriptor RuleDescriptor { get; } = new(
        "ATOMJS112",
        "Pure JavaScriptFunction недопустим для by-ref parameters",
        "Member '{0}' использует 'JavaScriptFunction(IsPure = true)', но ref/out/in parameters не поддерживаются как pure-path в текущем scaffold",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Текущая pure-модель generator scaffolding не трактует by-ref parameter semantics как допустимый pure fast-path."
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
        if (!node.ParameterList.Parameters.Any(static parameter => parameter.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.RefKeyword) || modifier.IsKind(SyntaxKind.OutKeyword) || modifier.IsKind(SyntaxKind.InKeyword)))) return;

        context.ReportDiagnostic(Diagnostic.Create(RuleDescriptor, node.Identifier.GetLocation(), node.Identifier.Text));
    }
}