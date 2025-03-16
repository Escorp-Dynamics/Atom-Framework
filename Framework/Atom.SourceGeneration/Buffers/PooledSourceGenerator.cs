using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;

namespace Atom.Buffers;

/// <summary>
/// Генератор кода для <see cref="PooledAttribute"/>.
/// </summary>
[Generator]
public class PooledSourceGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Инициализирует генератор.
    /// </summary>
    /// <param name="context">Контекст генератора.</param>
    public virtual void Initialize(IncrementalGeneratorInitializationContext context)
        => context.UseProvider(new PooledMethodSyntaxProvider(context), new PooledAttributeAnalyzerSyntaxProvider());
}