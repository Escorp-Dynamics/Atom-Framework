using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет базовую реализацию синтаксического провайдера анализатора.
/// </summary>
public abstract class AnalyzerSyntaxProvider : IAnalyzerSyntaxProvider
{
    /// <inheritdoc/>
    public abstract string Id { get; }

    /// <inheritdoc/>
    public abstract ImmutableArray<SyntaxKind> SyntaxKinds { get; }

    /// <inheritdoc/>
    public abstract DiagnosticDescriptor Rule { get; }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract void Execute(SyntaxNodeAnalysisContext context);
}