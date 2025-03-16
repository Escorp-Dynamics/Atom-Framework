using Atom.SourceGeneration;

namespace Atom.Architect.Components;

/// <summary>
/// Представляет провайдер анализатора для <see cref="ComponentOwnerAttribute"/>.
/// </summary>
public class ComponentOwnerAttributeAnalyzerSyntaxProvider : AttributeAnalyzerSyntaxProvider
{
    /// <inheritdoc/>
    public override string Id => "ComponentOwnerAnalyzer";

    /// <inheritdoc/>
    public override string Attribute => "ComponentOwner";
}