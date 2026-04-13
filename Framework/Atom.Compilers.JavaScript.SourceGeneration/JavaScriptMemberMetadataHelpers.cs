using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atom.Compilers.JavaScript.SourceGeneration;

internal static class JavaScriptMemberMetadataHelpers
{
    internal static string? GetContainingNamespace(ISymbol symbol)
        => symbol switch
        {
            ITypeSymbol typeSymbol => typeSymbol.ContainingNamespace.ToDisplayString(),
            _ => symbol.ContainingType?.ContainingNamespace.ToDisplayString(),
        };

    internal static IReadOnlyList<JavaScriptGeneratorScaffold.MemberMetadataEntry> CreateIgnoreEntries(MemberDeclarationSyntax declaration)
        => declaration switch
        {
            TypeDeclarationSyntax typeDeclaration => [new(typeDeclaration.Identifier.Text, GetMemberKind(typeDeclaration))],
            PropertyDeclarationSyntax propertyDeclaration => [new(propertyDeclaration.Identifier.Text, GetMemberKind(propertyDeclaration))],
            MethodDeclarationSyntax methodDeclaration => [new(methodDeclaration.Identifier.Text, GetMemberKind(methodDeclaration))],
            IndexerDeclarationSyntax => [new("Item", "Indexer")],
            EventDeclarationSyntax eventDeclaration => [new(eventDeclaration.Identifier.Text, "Event")],
            EventFieldDeclarationSyntax eventFieldDeclaration => [.. eventFieldDeclaration.Declaration.Variables.Select(static variable => new JavaScriptGeneratorScaffold.MemberMetadataEntry(variable.Identifier.Text, "Event"))],
            FieldDeclarationSyntax fieldDeclaration => [.. fieldDeclaration.Declaration.Variables.Select(static variable => new JavaScriptGeneratorScaffold.MemberMetadataEntry(variable.Identifier.Text, "Field"))],
            _ => [],
        };

    internal static JavaScriptGeneratorScaffold.MemberMetadataEntry CreatePropertyEntry(PropertyDeclarationSyntax declaration)
    {
        _ = JavaScriptAnalyzerSyntaxHelpers.TryGetExportName(declaration.AttributeLists, JavaScriptAttributeNames.Property, declaration.Identifier.Text, out var exportName);
        var constants = CreatePropertyConstants(declaration.AttributeLists);
        return new(declaration.Identifier.Text, "Property", exportName, constants);
    }

    internal static JavaScriptGeneratorScaffold.MemberMetadataEntry CreateFunctionEntry(MethodDeclarationSyntax declaration)
    {
        _ = JavaScriptAnalyzerSyntaxHelpers.TryGetExportName(declaration.AttributeLists, JavaScriptAttributeNames.Function, declaration.Identifier.Text, out var exportName);
        var constants = CreateFunctionConstants(declaration.AttributeLists);
        return new(declaration.Identifier.Text, "Method", exportName, constants);
    }

    private static IReadOnlyList<JavaScriptGeneratorScaffold.MetadataConstantEntry> CreatePropertyConstants(SyntaxList<AttributeListSyntax> attributeLists)
    {
        var isReadOnly = GetNamedBool(attributeLists, JavaScriptAttributeNames.Property, "IsReadOnly", defaultValue: false);
        var isRequired = GetNamedBool(attributeLists, JavaScriptAttributeNames.Property, "IsRequired", defaultValue: false);

        return
        [
            new("bool", "IsReadOnly", isReadOnly ? "true" : "false"),
            new("bool", "IsRequired", isRequired ? "true" : "false"),
        ];
    }

    private static IReadOnlyList<JavaScriptGeneratorScaffold.MetadataConstantEntry> CreateFunctionConstants(SyntaxList<AttributeListSyntax> attributeLists)
    {
        var isPure = GetNamedBool(attributeLists, JavaScriptAttributeNames.Function, "IsPure", defaultValue: false);
        var isInline = GetNamedBool(attributeLists, JavaScriptAttributeNames.Function, "IsInline", defaultValue: true);

        return
        [
            new("bool", "IsPure", isPure ? "true" : "false"),
            new("bool", "IsInline", isInline ? "true" : "false"),
        ];
    }

    private static bool GetNamedBool(SyntaxList<AttributeListSyntax> attributeLists, string attributeName, string argumentName, bool defaultValue)
    {
        foreach (var attributeList in attributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (!IsAttributeMatch(attribute, attributeName))
                    continue;

                return TryGetNamedBoolArgument(attribute, argumentName, out var value) ? value : defaultValue;
            }
        }

        return defaultValue;
    }

    private static bool IsAttributeMatch(AttributeSyntax attribute, string attributeName)
    {
        var currentName = JavaScriptAnalyzerSyntaxHelpers.GetAttributeSimpleName(attribute.Name);
        return currentName.Equals(attributeName, StringComparison.Ordinal) || currentName.Equals(attributeName + "Attribute", StringComparison.Ordinal);
    }

    private static bool TryGetNamedBoolArgument(AttributeSyntax attribute, string argumentName, out bool value)
    {
        value = default;
        if (attribute.ArgumentList is null) return false;

        foreach (var argument in attribute.ArgumentList.Arguments)
        {
            var identifier = argument.NameEquals?.Name.Identifier.Text;
            if (!string.Equals(identifier, argumentName, StringComparison.Ordinal))
                continue;

            if (argument.Expression is LiteralExpressionSyntax literal && literal.Token.Value is bool boolValue)
            {
                value = boolValue;
                return true;
            }
        }

        return false;
    }

    private static string GetMemberKind(MemberDeclarationSyntax declaration)
        => declaration switch
        {
            ClassDeclarationSyntax => "Class",
            StructDeclarationSyntax => "Struct",
            RecordDeclarationSyntax recordDeclaration when recordDeclaration.ClassOrStructKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StructKeyword) => "Struct",
            RecordDeclarationSyntax => "Class",
            InterfaceDeclarationSyntax => "Interface",
            PropertyDeclarationSyntax => "Property",
            MethodDeclarationSyntax => "Method",
            FieldDeclarationSyntax => "Field",
            IndexerDeclarationSyntax => "Indexer",
            EventDeclarationSyntax or EventFieldDeclarationSyntax => "Event",
            _ => "Unknown",
        };
}