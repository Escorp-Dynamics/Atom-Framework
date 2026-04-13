using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptPropertyReadOnlyWithoutGetterAnalyzerSyntaxProvider : AnalyzerSyntaxProvider
{
    public static DiagnosticDescriptor RuleDescriptor { get; } = new(
        "ATOMJS107",
        "Readonly JavaScriptProperty требует getter",
        "Member '{0}' использует 'JavaScriptProperty(IsReadOnly = true)', но не содержит getter",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Readonly export без getter не может сформировать читаемую JavaScript property surface."
    );

    public override string Id => RuleDescriptor.Id;

    public override ImmutableArray<SyntaxKind> SyntaxKinds => [SyntaxKind.PropertyDeclaration];

    public override DiagnosticDescriptor Rule => RuleDescriptor;

    public override void Execute(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not PropertyDeclarationSyntax node) return;
        if (JavaScriptAnalyzerSyntaxHelpers.IsIgnored(node.AttributeLists)) return;
        if (!JavaScriptAnalyzerSyntaxHelpers.HasAttribute(node.AttributeLists, JavaScriptAttributeNames.Property)) return;
        if (!HasReadOnlyFlag(node.AttributeLists)) return;
        if (HasGetter(node)) return;

        context.ReportDiagnostic(Diagnostic.Create(RuleDescriptor, node.Identifier.GetLocation(), node.Identifier.Text));
    }

    private static bool HasReadOnlyFlag(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (!IsJavaScriptPropertyAttribute(attribute))
                    continue;

                return TryGetReadOnlyArgument(attribute, out var isReadOnly) && isReadOnly;
            }
        }

        return false;
    }

    private static bool IsJavaScriptPropertyAttribute(AttributeSyntax attribute)
        => JavaScriptAnalyzerSyntaxHelpers.HasAttribute([SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute))], JavaScriptAttributeNames.Property);

    private static bool TryGetReadOnlyArgument(AttributeSyntax attribute, out bool isReadOnly)
    {
        isReadOnly = default;
        if (attribute.ArgumentList is null) return false;

        foreach (var argument in attribute.ArgumentList.Arguments)
        {
            if (!string.Equals(argument.NameEquals?.Name.Identifier.Text, "IsReadOnly", StringComparison.Ordinal))
                continue;

            if (argument.Expression is LiteralExpressionSyntax literal && literal.Token.Value is bool value)
            {
                isReadOnly = value;
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