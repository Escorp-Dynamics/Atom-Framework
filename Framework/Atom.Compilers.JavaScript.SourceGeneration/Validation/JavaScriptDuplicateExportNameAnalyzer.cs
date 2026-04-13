using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JavaScriptDuplicateExportNameAnalyzer : SourceAnalyzer<JavaScriptDuplicateExportNameAnalyzerSyntaxProvider>
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [JavaScriptDuplicateExportNameAnalyzerSyntaxProvider.RuleDescriptor];
}