using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JavaScriptPropertyRequiredWithoutGetterAnalyzer : SourceAnalyzer<JavaScriptPropertyRequiredWithoutGetterAnalyzerSyntaxProvider>
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [JavaScriptPropertyRequiredWithoutGetterAnalyzerSyntaxProvider.RuleDescriptor];
}