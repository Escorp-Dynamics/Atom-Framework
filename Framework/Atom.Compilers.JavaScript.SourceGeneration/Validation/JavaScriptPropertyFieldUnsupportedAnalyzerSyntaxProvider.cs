using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptPropertyFieldUnsupportedAnalyzerSyntaxProvider : AnalyzerSyntaxProvider
{
    public static DiagnosticDescriptor RuleDescriptor { get; } = new(
        "ATOMJS104",
        "Field-based JavaScriptProperty пока не поддерживается",
        "Field '{0}' с атрибутом 'JavaScriptProperty' пока не поддерживается текущим generator scaffold; используйте property",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Текущий generator scaffold обрабатывает только property-level JavaScriptProperty exports."
    );

    public override string Id => RuleDescriptor.Id;

    public override ImmutableArray<SyntaxKind> SyntaxKinds => [SyntaxKind.FieldDeclaration];

    public override DiagnosticDescriptor Rule => RuleDescriptor;

    public override void Execute(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not FieldDeclarationSyntax fieldNode) return;
        if (JavaScriptAnalyzerSyntaxHelpers.IsIgnored(fieldNode.AttributeLists)) return;
        if (!JavaScriptAnalyzerSyntaxHelpers.HasAttribute(fieldNode.AttributeLists, JavaScriptAttributeNames.Property)) return;

        foreach (var variable in fieldNode.Declaration.Variables)
            context.ReportDiagnostic(Diagnostic.Create(RuleDescriptor, variable.Identifier.GetLocation(), variable.Identifier.Text));
    }
}