using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptIgnoreAttributeAnalyzerSyntaxProvider : AttributeAnalyzerSyntaxProvider
{
    public override string Id => "ATOMJS005";

    public override string Attribute => JavaScriptAttributeNames.Ignore;

    public override DiagnosticDescriptor Rule { get; } = new(
        "ATOMJS005",
        "Обнаружен JavaScriptIgnore атрибут",
        "Обнаружен атрибут '{0}'",
        "Usage",
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: true,
        description: "Используется для регистрации JavaScriptIgnore в pipeline генерации."
    );
}