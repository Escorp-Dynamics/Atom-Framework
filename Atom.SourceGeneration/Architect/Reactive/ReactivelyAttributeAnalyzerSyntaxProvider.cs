using Atom.SourceGeneration;

namespace Atom.Architect.Reactive;

/// <summary>
/// Представляет провайдер реактивного атрибута анализатора.
/// </summary>
public class ReactivelyAttributeAnalyzerSyntaxProvider : AttributeAnalyzerSyntaxProvider
{
    /// <inheritdoc/>
    public override string Id => "ReactivelyAnalyzer";

    /// <inheritdoc/>
    public override string Attribute => "Reactively";
}