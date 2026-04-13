using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptFunctionMethodSyntaxProvider : MethodSyntaxProvider
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JavaScriptFunctionMethodSyntaxProvider(IncrementalGeneratorInitializationContext context) : base(context)
        => WithAttribute(JavaScriptAttributeNames.Function);

    protected override void OnExecute(SourceProductionContext context, string entityName, ImmutableArray<ISyntaxProviderInfo<IMethodSymbol, MethodDeclarationSyntax>> sources)
    {
        if (sources[0].Symbol is not { } symbol) return;

        var ns = JavaScriptMemberMetadataHelpers.GetContainingNamespace(symbol);
        var generatedTypeName = entityName + "JavaScriptFunctionMetadata";
        var members = sources.Select(static source => JavaScriptMemberMetadataHelpers.CreateFunctionEntry(source.Node)).ToArray();
        var source = JavaScriptGeneratorScaffold.BuildMemberSource(ns, entityName, generatedTypeName, JavaScriptAttributeNames.Function, members);
        context.AddSource(generatedTypeName + ".g.cs", SourceText.From(source, Encoding.UTF8));
    }
}