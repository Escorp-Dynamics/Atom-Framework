using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atom.Compilers.JavaScript.SourceGeneration;

internal static class JavaScriptTypeMetadataHelpers
{
    internal static IReadOnlyList<JavaScriptGeneratorScaffold.MetadataConstantEntry> CreateObjectConstants(ITypeSymbol symbol)
    {
        var isGlobalExportEnabled = GetNamedBool(symbol, JavaScriptAttributeNames.Object, "IsGlobalExportEnabled", defaultValue: false);

        return [new("bool", "IsGlobalExportEnabled", isGlobalExportEnabled ? "true" : "false")];
    }

    internal static IReadOnlyList<JavaScriptGeneratorScaffold.MetadataConstantEntry> CreateDictionaryConstants(ITypeSymbol symbol)
    {
        var isStringKeysOnly = GetNamedBool(symbol, JavaScriptAttributeNames.Dictionary, "IsStringKeysOnly", defaultValue: true);
        var isPreserveEnumerationOrder = GetNamedBool(symbol, JavaScriptAttributeNames.Dictionary, "IsPreserveEnumerationOrder", defaultValue: true);

        return
        [
            new("bool", "IsStringKeysOnly", isStringKeysOnly ? "true" : "false"),
            new("bool", "IsPreserveEnumerationOrder", isPreserveEnumerationOrder ? "true" : "false"),
        ];
    }

    internal static JavaScriptGeneratorScaffold.MemberMetadataEntry CreateObjectEntry(TypeDeclarationSyntax declaration)
    {
        _ = JavaScriptAnalyzerSyntaxHelpers.TryGetExportName(declaration.AttributeLists, JavaScriptAttributeNames.Object, declaration.Identifier.Text, out var exportName);
        return new(declaration.Identifier.Text, GetTypeKind(declaration), exportName);
    }

    internal static JavaScriptGeneratorScaffold.MemberMetadataEntry CreateDictionaryEntry(TypeDeclarationSyntax declaration)
        => new(declaration.Identifier.Text, GetTypeKind(declaration), declaration.Identifier.Text);

    private static bool GetNamedBool(ITypeSymbol symbol, string attributeName, string parameterName, bool defaultValue)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (!string.Equals(attribute.AttributeClass?.Name, attributeName + "Attribute", StringComparison.Ordinal))
                continue;

            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (!string.Equals(namedArgument.Key, parameterName, StringComparison.Ordinal))
                    continue;

                if (namedArgument.Value.Value is bool value)
                    return value;
            }
        }

        return defaultValue;
    }

    private static string GetTypeKind(TypeDeclarationSyntax declaration)
        => declaration switch
        {
            ClassDeclarationSyntax => "Class",
            StructDeclarationSyntax => "Struct",
            RecordDeclarationSyntax recordDeclaration when recordDeclaration.ClassOrStructKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StructKeyword) => "Struct",
            RecordDeclarationSyntax => "Class",
            InterfaceDeclarationSyntax => "Interface",
            _ => declaration.Kind().ToString(),
        };
}