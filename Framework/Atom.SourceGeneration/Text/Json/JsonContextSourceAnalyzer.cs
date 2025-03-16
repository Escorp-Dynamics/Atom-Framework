using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Text.Json;

/// <summary>
/// Представляет анализатор для <see cref="JsonContextAttribute"/>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class JsonContextSourceAnalyzer : SourceAnalyzer<JsonContextAttributeAnalyzerSyntaxProvider>
{
    /// <summary>
    /// Поддерживаемые дескрипторы диагностики.
    /// </summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [AttributeAnalyzerSyntaxProvider.DefaultRule];
}