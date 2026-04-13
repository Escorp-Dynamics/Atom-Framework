using System.Collections.Immutable;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.Compilers.JavaScript.SourceGeneration;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JavaScriptObjectAttributeAnalyzer : SourceAnalyzer<JavaScriptObjectAttributeAnalyzerSyntaxProvider>
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [new JavaScriptObjectAttributeAnalyzerSyntaxProvider().Rule];
}