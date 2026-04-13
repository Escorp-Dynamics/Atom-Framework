using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JavaScriptFunctionPureVoidAnalyzer : SourceAnalyzer<JavaScriptFunctionPureVoidAnalyzerSyntaxProvider>
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [JavaScriptFunctionPureVoidAnalyzerSyntaxProvider.RuleDescriptor];
}