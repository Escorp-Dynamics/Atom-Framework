using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JavaScriptFunctionPureTaskLikeAnalyzer : SourceAnalyzer<JavaScriptFunctionPureTaskLikeAnalyzerSyntaxProvider>
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [JavaScriptFunctionPureTaskLikeAnalyzerSyntaxProvider.RuleDescriptor];
}