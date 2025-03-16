using Atom.SourceGeneration;

namespace Atom.Architect.Reactive;

/// <summary>
/// Представляет провайдер анализатора для <see cref="ReactivelyAttribute"/>.
/// </summary>
public class ReactivelyAttributeAnalyzerSyntaxProvider : AttributeAnalyzerSyntaxProvider
{
    /// <inheritdoc/>
    public override string Id => "ReactivelyAnalyzer";

    /// <inheritdoc/>
    public override string Attribute => "Reactively";
}