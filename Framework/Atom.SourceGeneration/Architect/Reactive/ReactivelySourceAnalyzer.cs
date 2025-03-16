using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Architect.Reactive;

/// <summary>
/// Представляет анализатор для <see cref="ReactivelyAttribute"/>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ReactivelySourceAnalyzer : SourceAnalyzer<ReactivelyAttributeAnalyzerSyntaxProvider>
{
    /// <summary>
    /// Поддерживаемые дескрипторы диагностики.
    /// </summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [AttributeAnalyzerSyntaxProvider.DefaultRule];
}