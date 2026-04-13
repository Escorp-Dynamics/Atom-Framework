using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptDictionaryTypeSyntaxProvider : TypeSyntaxProvider
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JavaScriptDictionaryTypeSyntaxProvider(IncrementalGeneratorInitializationContext context) : base(context)
        => WithAttribute(JavaScriptAttributeNames.Dictionary);

    protected override void OnExecute(SourceProductionContext context, string entityName, ImmutableArray<ISyntaxProviderInfo<ITypeSymbol, TypeDeclarationSyntax>> sources)
    {
        if (sources[0].Symbol is not { } symbol) return;

        var ns = JavaScriptMemberMetadataHelpers.GetContainingNamespace(symbol);
        var generatedTypeName = entityName + "JavaScriptDictionaryMetadata";
        var members = sources.Select(static source => JavaScriptTypeMetadataHelpers.CreateDictionaryEntry(source.Node)).ToArray();
        var constants = JavaScriptTypeMetadataHelpers.CreateDictionaryConstants(symbol);
        var source = JavaScriptGeneratorScaffold.BuildMemberSource(ns, entityName, generatedTypeName, JavaScriptAttributeNames.Dictionary, members, constants);
        context.AddSource(generatedTypeName + ".g.cs", SourceText.From(source, Encoding.UTF8));
    }
}