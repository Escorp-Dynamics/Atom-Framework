using System.Runtime.CompilerServices;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;

namespace Atom.Compilers.JavaScript.SourceGeneration;

[Generator]
public sealed class JavaScriptPropertySourceGenerator : IIncrementalGenerator
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Initialize(IncrementalGeneratorInitializationContext context)
        => context.UseProvider(new JavaScriptPropertyPropertySyntaxProvider(context));
}