using System.Collections.Immutable;
using Atom.Architect.Reactive;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.SourceGeneration.Architect.Reactive;

/// <summary>
/// Представляет анализатор для реактивных полей.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ReactivelySourceAnalyzer : SourceAnalyzer<ReactivelyAttributeAnalyzerSyntaxProvider>
{
    /// <summary>
    /// Поддерживаемые дескрипторы диагностики.
    /// </summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [AttributeAnalyzerSyntaxProvider.DefaultRule];
}