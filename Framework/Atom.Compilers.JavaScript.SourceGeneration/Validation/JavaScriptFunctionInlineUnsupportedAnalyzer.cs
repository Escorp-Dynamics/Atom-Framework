using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JavaScriptFunctionInlineUnsupportedAnalyzer : SourceAnalyzer<JavaScriptFunctionInlineUnsupportedAnalyzerSyntaxProvider>
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [JavaScriptFunctionInlineUnsupportedAnalyzerSyntaxProvider.RuleDescriptor];
}