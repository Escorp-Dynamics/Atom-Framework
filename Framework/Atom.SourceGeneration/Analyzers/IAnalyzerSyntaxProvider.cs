using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет базовый интерфейс для реализации синтаксических провайдеров анализатора.
/// </summary>
public interface IAnalyzerSyntaxProvider
{
    /// <summary>
    /// Идентификатор анализатора.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Типы синтаксических конструкций, засекаемых анализатором.
    /// </summary>
    ImmutableArray<SyntaxKind> SyntaxKinds { get; }

    /// <summary>
    /// Дескриптор диагностики.
    /// </summary>
    DiagnosticDescriptor Rule { get; }

    /// <summary>
    /// Выполняет анализ кода.
    /// </summary>
    /// <param name="context">Контекст анализа.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Execute(SyntaxNodeAnalysisContext context);
}