using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atom.Compilers.JavaScript.SourceGeneration;

internal static class JavaScriptAnalyzerSyntaxHelpers
{
    internal static string GetAttributeSimpleName(NameSyntax name)
        => name switch
        {
            IdentifierNameSyntax identifierName => identifierName.Identifier.Text,
            QualifiedNameSyntax qualifiedName => GetAttributeSimpleName(qualifiedName.Right),
            AliasQualifiedNameSyntax aliasQualifiedName => aliasQualifiedName.Name.Identifier.Text,
            _ => name.ToString(),
        };

    internal static bool HasAttribute(SyntaxList<AttributeListSyntax> attributeLists, string attributeName)
    {
        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var currentName = GetAttributeSimpleName(attribute.Name);
                if (currentName.Equals(attributeName, StringComparison.Ordinal) || currentName.Equals(attributeName + "Attribute", StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    internal static bool HasAttribute(ISymbol symbol, string attributeName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            var currentName = attribute.AttributeClass?.Name;
            if (string.IsNullOrEmpty(currentName))
                continue;

            if (currentName.Equals(attributeName + "Attribute", StringComparison.Ordinal) || currentName.Equals(attributeName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    internal static bool IsIgnored(SyntaxList<AttributeListSyntax> attributeLists)
        => HasAttribute(attributeLists, JavaScriptAttributeNames.Ignore);

    internal static bool IsIgnored(ISymbol symbol)
        => HasAttribute(symbol, JavaScriptAttributeNames.Ignore);

    internal static bool TryGetExportName(SyntaxList<AttributeListSyntax> attributeLists, string attributeName, string defaultName, out string exportName)
    {
        exportName = defaultName;

        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var currentName = GetAttributeSimpleName(attribute.Name);
                if (!currentName.Equals(attributeName, StringComparison.Ordinal) && !currentName.Equals(attributeName + "Attribute", StringComparison.Ordinal))
                    continue;

                if (attribute.ArgumentList?.Arguments.Count > 0 && attribute.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal && literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression))
                    exportName = literal.Token.ValueText;

                return true;
            }
        }

        return false;
    }

    internal static bool TryGetExportName(ISymbol symbol, string attributeName, string defaultName, out string exportName)
    {
        exportName = defaultName;

        foreach (var attribute in symbol.GetAttributes())
        {
            var currentName = attribute.AttributeClass?.Name;
            if (string.IsNullOrEmpty(currentName))
                continue;

            if (!currentName.Equals(attributeName + "Attribute", StringComparison.Ordinal) && !currentName.Equals(attributeName, StringComparison.Ordinal))
                continue;

            if (attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string value)
                exportName = value;

            return true;
        }

        return false;
    }
}