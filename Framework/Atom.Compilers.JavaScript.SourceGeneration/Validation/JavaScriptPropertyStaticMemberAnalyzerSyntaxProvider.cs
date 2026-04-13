using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptPropertyStaticMemberAnalyzerSyntaxProvider : AnalyzerSyntaxProvider
{
    public static DiagnosticDescriptor RuleDescriptor { get; } = new(
        "ATOMJS102",
        "Неподдерживаемый static JavaScriptProperty",
        "Static member '{0}' с атрибутом 'JavaScriptProperty' пока не поддерживается генератором host bindings",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Текущий JavaScript property binding scaffold рассчитан на instance members."
    );

    public override string Id => RuleDescriptor.Id;

    public override ImmutableArray<SyntaxKind> SyntaxKinds =>
    [
        SyntaxKind.PropertyDeclaration,
        SyntaxKind.FieldDeclaration,
    ];

    public override DiagnosticDescriptor Rule => RuleDescriptor;

    public override void Execute(SyntaxNodeAnalysisContext context)
    {
        switch (context.Node)
        {
            case PropertyDeclarationSyntax propertyNode when IsStatic(propertyNode.Modifiers)
                && !JavaScriptAnalyzerSyntaxHelpers.IsIgnored(propertyNode.AttributeLists)
                && JavaScriptAnalyzerSyntaxHelpers.HasAttribute(propertyNode.AttributeLists, JavaScriptAttributeNames.Property):
                context.ReportDiagnostic(Diagnostic.Create(RuleDescriptor, propertyNode.Identifier.GetLocation(), propertyNode.Identifier.Text));
                break;

            case FieldDeclarationSyntax fieldNode when IsStatic(fieldNode.Modifiers)
                && !JavaScriptAnalyzerSyntaxHelpers.IsIgnored(fieldNode.AttributeLists)
                && JavaScriptAnalyzerSyntaxHelpers.HasAttribute(fieldNode.AttributeLists, JavaScriptAttributeNames.Property):
                foreach (var variable in fieldNode.Declaration.Variables)
                    context.ReportDiagnostic(Diagnostic.Create(RuleDescriptor, variable.Identifier.GetLocation(), variable.Identifier.Text));
                break;
        }
    }

    private static bool IsStatic(SyntaxTokenList modifiers) => modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.StaticKeyword));
}