using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptPropertyPropertySyntaxProvider : PropertySyntaxProvider
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JavaScriptPropertyPropertySyntaxProvider(IncrementalGeneratorInitializationContext context) : base(context)
        => WithAttribute(JavaScriptAttributeNames.Property);

    protected override void OnExecute(SourceProductionContext context, string entityName, ImmutableArray<ISyntaxProviderInfo<IPropertySymbol, PropertyDeclarationSyntax>> sources)
    {
        if (sources[0].Symbol is not { } symbol) return;

        var ns = JavaScriptMemberMetadataHelpers.GetContainingNamespace(symbol);
        var generatedTypeName = entityName + "JavaScriptPropertyMetadata";
        var members = sources.Select(static source => JavaScriptMemberMetadataHelpers.CreatePropertyEntry(source.Node)).ToArray();
        var source = JavaScriptGeneratorScaffold.BuildMemberSource(ns, entityName, generatedTypeName, JavaScriptAttributeNames.Property, members);
        context.AddSource(generatedTypeName + ".g.cs", SourceText.From(source, Encoding.UTF8));
    }
}