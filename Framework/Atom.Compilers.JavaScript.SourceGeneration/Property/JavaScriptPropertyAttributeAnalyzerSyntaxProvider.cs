using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptPropertyAttributeAnalyzerSyntaxProvider : AttributeAnalyzerSyntaxProvider
{
    public override string Id => "ATOMJS003";

    public override string Attribute => JavaScriptAttributeNames.Property;

    public override DiagnosticDescriptor Rule { get; } = new(
        "ATOMJS003",
        "Обнаружен JavaScriptProperty атрибут",
        "Обнаружен атрибут '{0}'",
        "Usage",
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: true,
        description: "Используется для регистрации JavaScriptProperty в pipeline генерации."
    );
}