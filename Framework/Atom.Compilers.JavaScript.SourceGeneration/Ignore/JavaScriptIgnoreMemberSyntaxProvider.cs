using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptIgnoreMemberSyntaxProvider : MemberSyntaxProvider
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JavaScriptIgnoreMemberSyntaxProvider(IncrementalGeneratorInitializationContext context) : base(context)
        => WithAttribute(JavaScriptAttributeNames.Ignore);

    protected override void OnExecute(SourceProductionContext context, string entityName, ImmutableArray<ISyntaxProviderInfo<ISymbol, MemberDeclarationSyntax>> sources)
    {
        if (sources[0].Symbol is not { } symbol) return;

        var ns = JavaScriptMemberMetadataHelpers.GetContainingNamespace(symbol);

        var generatedTypeName = entityName + "JavaScriptIgnoreMetadata";
        var members = sources.SelectMany(static source => JavaScriptMemberMetadataHelpers.CreateIgnoreEntries(source.Node)).ToArray();
        var source = JavaScriptGeneratorScaffold.BuildMemberSource(ns, entityName, generatedTypeName, JavaScriptAttributeNames.Ignore, members);
        context.AddSource(generatedTypeName + ".g.cs", SourceText.From(source, Encoding.UTF8));
    }
}