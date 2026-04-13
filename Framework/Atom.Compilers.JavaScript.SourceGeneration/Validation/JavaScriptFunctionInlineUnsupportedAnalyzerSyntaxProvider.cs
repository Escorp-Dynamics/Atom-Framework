using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptFunctionInlineUnsupportedAnalyzerSyntaxProvider : AnalyzerSyntaxProvider
{
    public static DiagnosticDescriptor RuleDescriptor { get; } = new(
        "ATOMJS108",
        "Inlining JavaScriptFunction недоступен для abstract/interface methods",
        "Member '{0}' использует 'JavaScriptFunction(IsInline = true)', но abstract/interface methods не имеют inline body для текущего adapter scaffold",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Текущая inline-модель генератора требует доступное method body на compile-time этапе."
    );

    public override string Id => RuleDescriptor.Id;

    public override ImmutableArray<SyntaxKind> SyntaxKinds => [SyntaxKind.MethodDeclaration];

    public override DiagnosticDescriptor Rule => RuleDescriptor;

    public override void Execute(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MethodDeclarationSyntax node) return;
        if (JavaScriptAnalyzerSyntaxHelpers.IsIgnored(node.AttributeLists)) return;
        if (!JavaScriptAnalyzerSyntaxHelpers.HasAttribute(node.AttributeLists, JavaScriptAttributeNames.Function)) return;
        if (!IsInlineEnabled(node.AttributeLists)) return;
        if (!IsAbstractOrInterfaceMethod(node)) return;

        context.ReportDiagnostic(Diagnostic.Create(RuleDescriptor, node.Identifier.GetLocation(), node.Identifier.Text));
    }

    private static bool IsInlineEnabled(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (!IsJavaScriptFunctionAttribute(attribute))
                    continue;

                if (TryGetInlineArgument(attribute, out var isInline))
                    return isInline;

                return true;
            }
        }

        return false;
    }

    private static bool IsJavaScriptFunctionAttribute(AttributeSyntax attribute)
        => JavaScriptAnalyzerSyntaxHelpers.HasAttribute([SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute))], JavaScriptAttributeNames.Function);

    private static bool TryGetInlineArgument(AttributeSyntax attribute, out bool isInline)
    {
        isInline = default;
        if (attribute.ArgumentList is null) return false;

        foreach (var argument in attribute.ArgumentList.Arguments)
        {
            if (!string.Equals(argument.NameEquals?.Name.Identifier.Text, "IsInline", StringComparison.Ordinal))
                continue;

            if (argument.Expression is LiteralExpressionSyntax literal && literal.Token.Value is bool value)
            {
                isInline = value;
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool IsAbstractOrInterfaceMethod(MethodDeclarationSyntax node)
    {
        if (node.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.AbstractKeyword)))
            return true;

        return node.Parent is InterfaceDeclarationSyntax;
    }
}