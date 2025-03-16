using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Architect.Components;

/// <summary>
/// Представляет анализатор для <see cref="ComponentAttribute"/>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ComponentSourceAnalyzer : SourceAnalyzer<ComponentAttributeAnalyzerSyntaxProvider>
{
    /// <summary>
    /// Поддерживаемые дескрипторы диагностики.
    /// </summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [AttributeAnalyzerSyntaxProvider.DefaultRule];
}