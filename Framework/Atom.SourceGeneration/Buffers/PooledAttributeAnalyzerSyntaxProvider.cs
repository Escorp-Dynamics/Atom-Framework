using Atom.SourceGeneration;

namespace Atom.Buffers;

/// <summary>
/// Представляет провайдер анализатора для <see cref="PooledAttribute"/>.
/// </summary>
public class PooledAttributeAnalyzerSyntaxProvider : AttributeAnalyzerSyntaxProvider
{
    /// <inheritdoc/>
    public override string Id => "PooledAnalyzer";

    /// <inheritdoc/>
    public override string Attribute => "Pooled";
}