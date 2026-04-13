using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptEmptyExportNameAnalyzerSyntaxProvider : AnalyzerSyntaxProvider
{
    public static DiagnosticDescriptor RuleDescriptor { get; } = new(
        "ATOMJS105",
        "Пустое JavaScript export name недопустимо",
        "Explicit JavaScript export name для '{0}' не должен быть пустым",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Явно заданные JavaScript export names должны содержать непустое значение."
    );

    public override string Id => RuleDescriptor.Id;

    public override ImmutableArray<SyntaxKind> SyntaxKinds =>
    [
        SyntaxKind.ClassDeclaration,
        SyntaxKind.StructDeclaration,
        SyntaxKind.InterfaceDeclaration,
        SyntaxKind.PropertyDeclaration,
        SyntaxKind.MethodDeclaration,
        SyntaxKind.FieldDeclaration,
    ];

    public override DiagnosticDescriptor Rule => RuleDescriptor;

    public override void Execute(SyntaxNodeAnalysisContext context)
    {
        switch (context.Node)
        {
            case TypeDeclarationSyntax typeNode:
                if (JavaScriptAnalyzerSyntaxHelpers.IsIgnored(typeNode.AttributeLists)) return;
                ExecuteType(typeNode, context);
                return;

            case PropertyDeclarationSyntax propertyNode:
                if (JavaScriptAnalyzerSyntaxHelpers.IsIgnored(propertyNode.AttributeLists)) return;
                ReportIfEmpty(propertyNode.AttributeLists, JavaScriptAttributeNames.Property, context);
                return;

            case MethodDeclarationSyntax methodNode:
                if (JavaScriptAnalyzerSyntaxHelpers.IsIgnored(methodNode.AttributeLists)) return;
                ReportIfEmpty(methodNode.AttributeLists, JavaScriptAttributeNames.Function, context);
                return;

            case FieldDeclarationSyntax fieldNode:
                if (JavaScriptAnalyzerSyntaxHelpers.IsIgnored(fieldNode.AttributeLists)) return;
                ReportIfEmpty(fieldNode.AttributeLists, JavaScriptAttributeNames.Property, context);
                return;
        }
    }

    private static void ExecuteType(TypeDeclarationSyntax typeNode, SyntaxNodeAnalysisContext context)
    {
        ReportIfEmpty(typeNode.AttributeLists, JavaScriptAttributeNames.Object, context);
        ReportIfEmpty(typeNode.AttributeLists, JavaScriptAttributeNames.Dictionary, context);
    }

    private static void ReportIfEmpty(SyntaxList<AttributeListSyntax> attributeLists, string attributeName, SyntaxNodeAnalysisContext context)
    {
        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (!IsAttributeMatch(attribute, attributeName))
                    continue;

                if (TryGetEmptyLiteral(attribute, out var literal))
                    context.ReportDiagnostic(Diagnostic.Create(RuleDescriptor, literal.GetLocation(), attributeName));

                return;
            }
        }
    }

    private static bool IsAttributeMatch(AttributeSyntax attribute, string attributeName)
    {
        var currentName = attribute.Name.ToString();
        return currentName.Equals(attributeName, StringComparison.Ordinal) || currentName.Equals(attributeName + "Attribute", StringComparison.Ordinal);
    }

    private static bool TryGetEmptyLiteral(AttributeSyntax attribute, out LiteralExpressionSyntax literal)
    {
        literal = default!;

        if (attribute.ArgumentList?.Arguments.Count is 0 or null)
            return false;

        if (attribute.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax candidate)
            return false;

        if (!candidate.IsKind(SyntaxKind.StringLiteralExpression))
            return false;

        if (!string.IsNullOrEmpty(candidate.Token.ValueText))
            return false;

        literal = candidate;
        return true;
    }
}