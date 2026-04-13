using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;

namespace Atom.Compilers.JavaScript.SourceGeneration;

public sealed class JavaScriptDictionaryAttributeAnalyzerSyntaxProvider : AttributeAnalyzerSyntaxProvider
{
    public override string Id => "ATOMJS002";

    public override string Attribute => JavaScriptAttributeNames.Dictionary;

    public override DiagnosticDescriptor Rule { get; } = new(
        "ATOMJS002",
        "Обнаружен JavaScriptDictionary атрибут",
        "Обнаружен атрибут '{0}'",
        "Usage",
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: true,
        description: "Используется для регистрации JavaScriptDictionary в pipeline генерации."
    );
}