using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Architect.Components;

/// <summary>
/// Представляет анализатор для <see cref="ComponentOwnerAttribute"/>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ComponentOwnerSourceAnalyzer : SourceAnalyzer<ComponentOwnerAttributeAnalyzerSyntaxProvider>
{
    /// <summary>
    /// Поддерживаемые дескрипторы диагностики.
    /// </summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [AttributeAnalyzerSyntaxProvider.DefaultRule];
}