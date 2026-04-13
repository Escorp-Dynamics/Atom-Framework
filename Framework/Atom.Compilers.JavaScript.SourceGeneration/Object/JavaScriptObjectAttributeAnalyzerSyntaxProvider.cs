using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptObjectAttributeAnalyzerSyntaxProvider : AttributeAnalyzerSyntaxProvider
{
    public override string Id => "ATOMJS001";

    public override string Attribute => JavaScriptAttributeNames.Object;

    public override DiagnosticDescriptor Rule { get; } = new(
        "ATOMJS001",
        "Обнаружен JavaScriptObject атрибут",
        "Обнаружен атрибут '{0}'",
        "Usage",
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: true,
        description: "Используется для регистрации JavaScriptObject в pipeline генерации."
    );
}