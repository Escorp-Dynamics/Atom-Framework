using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptObjectTypeSyntaxProvider : TypeSyntaxProvider
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JavaScriptObjectTypeSyntaxProvider(IncrementalGeneratorInitializationContext context) : base(context)
        => WithAttribute(JavaScriptAttributeNames.Object);

    protected override void OnExecute(SourceProductionContext context, string entityName, ImmutableArray<ISyntaxProviderInfo<ITypeSymbol, TypeDeclarationSyntax>> sources)
    {
        if (sources[0].Symbol is not { } symbol) return;

        var ns = JavaScriptMemberMetadataHelpers.GetContainingNamespace(symbol);
        var generatedTypeName = entityName + "JavaScriptObjectMetadata";
        var members = sources.Select(static source => JavaScriptTypeMetadataHelpers.CreateObjectEntry(source.Node)).ToArray();
        var constants = JavaScriptTypeMetadataHelpers.CreateObjectConstants(symbol);
        var source = JavaScriptGeneratorScaffold.BuildMemberSource(ns, entityName, generatedTypeName, JavaScriptAttributeNames.Object, members, constants);
        context.AddSource(generatedTypeName + ".g.cs", SourceText.From(source, Encoding.UTF8));
    }
}