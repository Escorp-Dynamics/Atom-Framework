using Atom.SourceGeneration;

namespace Atom.Architect.Components;

/// <summary>
/// Представляет провайдер анализатора для <see cref="ComponentAttribute"/>.
/// </summary>
public class ComponentAttributeAnalyzerSyntaxProvider : AttributeAnalyzerSyntaxProvider
{
    /// <inheritdoc/>
    public override string Id => "ComponentAnalyzer";

    /// <inheritdoc/>
    public override string Attribute => "Component";
}