#pragma warning disable RS1025, RS1026

using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет базовую реализацию анализатора исходного кода.
/// </summary>
public abstract class SourceAnalyzer : DiagnosticAnalyzer;

/// <summary>
/// Представляет базовую реализацию анализатора исходного кода.
/// </summary>
/// <typeparam name="TAnalyzerProvider">Тип провайдера.</typeparam>
public abstract class SourceAnalyzer<TAnalyzerProvider> : SourceAnalyzer where TAnalyzerProvider : IAnalyzerSyntaxProvider, new()
{
    /// <summary>
    /// Инициализирует анализатор.
    /// </summary>
    /// <param name="context">Контекст диагностики.</param>
    public override void Initialize([NotNull] AnalysisContext context) => context.UseProvider(new TAnalyzerProvider());
}