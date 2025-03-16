using Atom.SourceGeneration;

namespace Atom.Text.Json;

/// <summary>
/// Представляет провайдер анализатора для <see cref="JsonContextAttribute"/>.
/// </summary>
public class JsonContextAttributeAnalyzerSyntaxProvider : AttributeAnalyzerSyntaxProvider
{
    /// <inheritdoc/>
    public override string Id => "JsonContextAnalyzer";

    /// <inheritdoc/>
    public override string Attribute => "JsonContext";
}