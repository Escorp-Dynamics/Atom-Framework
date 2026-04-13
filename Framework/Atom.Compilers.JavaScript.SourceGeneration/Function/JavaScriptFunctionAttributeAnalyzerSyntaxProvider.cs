using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptFunctionAttributeAnalyzerSyntaxProvider : AttributeAnalyzerSyntaxProvider
{
    public override string Id => "ATOMJS004";

    public override string Attribute => JavaScriptAttributeNames.Function;

    public override DiagnosticDescriptor Rule { get; } = new(
        "ATOMJS004",
        "Обнаружен JavaScriptFunction атрибут",
        "Обнаружен атрибут '{0}'",
        "Usage",
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: true,
        description: "Используется для регистрации JavaScriptFunction в pipeline генерации."
    );
}