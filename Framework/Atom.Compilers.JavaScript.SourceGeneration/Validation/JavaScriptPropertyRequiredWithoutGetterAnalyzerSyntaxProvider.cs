using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptPropertyRequiredWithoutGetterAnalyzerSyntaxProvider : AnalyzerSyntaxProvider
{
    public static DiagnosticDescriptor RuleDescriptor { get; } = new(
        "ATOMJS109",
        "Required JavaScriptProperty требует getter",
        "Member '{0}' использует 'JavaScriptProperty(IsRequired = true)', но не содержит getter",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Required materialization без getter не даёт стабильной читаемой property surface."
    );

    public override string Id => RuleDescriptor.Id;

    public override ImmutableArray<SyntaxKind> SyntaxKinds => [SyntaxKind.PropertyDeclaration];

    public override DiagnosticDescriptor Rule => RuleDescriptor;

    public override void Execute(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not PropertyDeclarationSyntax node) return;
        if (JavaScriptAnalyzerSyntaxHelpers.IsIgnored(node.AttributeLists)) return;
        if (!JavaScriptAnalyzerSyntaxHelpers.HasAttribute(node.AttributeLists, JavaScriptAttributeNames.Property)) return;
        if (!HasRequiredFlag(node.AttributeLists)) return;
        if (HasGetter(node)) return;

        context.ReportDiagnostic(Diagnostic.Create(RuleDescriptor, node.Identifier.GetLocation(), node.Identifier.Text));
    }

    private static bool HasRequiredFlag(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (!IsJavaScriptPropertyAttribute(attribute))
                    continue;

                return TryGetRequiredArgument(attribute, out var isRequired) && isRequired;
            }
        }

        return false;
    }

    private static bool IsJavaScriptPropertyAttribute(AttributeSyntax attribute)
        => JavaScriptAnalyzerSyntaxHelpers.HasAttribute([SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute))], JavaScriptAttributeNames.Property);

    private static bool TryGetRequiredArgument(AttributeSyntax attribute, out bool isRequired)
    {
        isRequired = default;
        if (attribute.ArgumentList is null) return false;

        foreach (var argument in attribute.ArgumentList.Arguments)
        {
            if (!string.Equals(argument.NameEquals?.Name.Identifier.Text, "IsRequired", StringComparison.Ordinal))
                continue;

            if (argument.Expression is LiteralExpressionSyntax literal && literal.Token.Value is bool value)
            {
                isRequired = value;
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool HasGetter(PropertyDeclarationSyntax node)
    {
        if (node.ExpressionBody is not null)
            return true;

        if (node.AccessorList is null)
            return false;

        return node.AccessorList.Accessors.Any(static accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
    }
}