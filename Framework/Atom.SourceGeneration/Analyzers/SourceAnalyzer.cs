using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Initialize([NotNull] AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.UseProvider(new TAnalyzerProvider());
    }
}