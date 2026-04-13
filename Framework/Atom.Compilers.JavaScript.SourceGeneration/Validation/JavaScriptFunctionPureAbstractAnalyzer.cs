using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JavaScriptFunctionPureAbstractAnalyzer : SourceAnalyzer<JavaScriptFunctionPureAbstractAnalyzerSyntaxProvider>
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [JavaScriptFunctionPureAbstractAnalyzerSyntaxProvider.RuleDescriptor];
}