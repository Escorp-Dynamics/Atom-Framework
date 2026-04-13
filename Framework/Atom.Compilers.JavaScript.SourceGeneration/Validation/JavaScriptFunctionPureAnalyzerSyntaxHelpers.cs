using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atom.Compilers.JavaScript.SourceGeneration;

internal static class JavaScriptFunctionPureAnalyzerSyntaxHelpers
{
    internal static bool HasPureFlag(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (!IsJavaScriptFunctionAttribute(attribute))
                    continue;

                return TryGetPureArgument(attribute, out var isPure) && isPure;
            }
        }

        return false;
    }

    internal static bool IsAbstractOrInterfaceMethod(MethodDeclarationSyntax node)
    {
        if (node.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.AbstractKeyword)))
            return true;

        return node.Parent is InterfaceDeclarationSyntax;
    }

    private static bool IsJavaScriptFunctionAttribute(AttributeSyntax attribute)
        => JavaScriptAnalyzerSyntaxHelpers.HasAttribute([SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute))], JavaScriptAttributeNames.Function);

    private static bool TryGetPureArgument(AttributeSyntax attribute, out bool isPure)
    {
        isPure = default;
        if (attribute.ArgumentList is null) return false;

        foreach (var argument in attribute.ArgumentList.Arguments)
        {
            if (!string.Equals(argument.NameEquals?.Name.Identifier.Text, "IsPure", StringComparison.Ordinal))
                continue;

            if (argument.Expression is LiteralExpressionSyntax literal && literal.Token.Value is bool value)
            {
                isPure = value;
                return true;
            }

            return false;
        }

        return false;
    }
}