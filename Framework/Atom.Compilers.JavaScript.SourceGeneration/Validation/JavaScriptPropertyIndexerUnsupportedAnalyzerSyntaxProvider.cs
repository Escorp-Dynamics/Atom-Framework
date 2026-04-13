using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptPropertyIndexerUnsupportedAnalyzerSyntaxProvider : AnalyzerSyntaxProvider
{
    public static DiagnosticDescriptor RuleDescriptor { get; } = new(
        "ATOMJS106",
        "Indexer-based JavaScriptProperty пока не поддерживается",
        "Indexer с атрибутом 'JavaScriptProperty' пока не поддерживается текущим generator scaffold; используйте именованное property-обёртывание",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Текущий property generator scaffold работает только с обычными property declarations и не строит metadata для indexers."
    );

    public override string Id => RuleDescriptor.Id;

    public override ImmutableArray<SyntaxKind> SyntaxKinds => [SyntaxKind.IndexerDeclaration];

    public override DiagnosticDescriptor Rule => RuleDescriptor;

    public override void Execute(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not IndexerDeclarationSyntax node) return;
        if (JavaScriptAnalyzerSyntaxHelpers.IsIgnored(node.AttributeLists)) return;
        if (!JavaScriptAnalyzerSyntaxHelpers.HasAttribute(node.AttributeLists, JavaScriptAttributeNames.Property)) return;

        context.ReportDiagnostic(Diagnostic.Create(RuleDescriptor, node.ThisKeyword.GetLocation()));
    }
}