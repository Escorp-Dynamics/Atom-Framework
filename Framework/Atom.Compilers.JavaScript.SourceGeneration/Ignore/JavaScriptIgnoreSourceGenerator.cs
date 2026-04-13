using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;

namespace Atom.Compilers.JavaScript.SourceGeneration;

[Generator]
public sealed class JavaScriptIgnoreSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
        => context.UseProvider(new JavaScriptIgnoreMemberSyntaxProvider(context), new JavaScriptIgnoreAttributeAnalyzerSyntaxProvider());
}