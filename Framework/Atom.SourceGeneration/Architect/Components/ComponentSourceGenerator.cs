using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;

namespace Atom.Architect.Components;

/// <summary>
/// Генератор кода для <see cref="ComponentAttribute"/>.
/// </summary>
[Generator]
public class ComponentSourceGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Инициализирует генератор.
    /// </summary>
    /// <param name="context">Контекст генератора.</param>
    public virtual void Initialize(IncrementalGeneratorInitializationContext context)
        => context.UseProvider(new ComponentTypeSyntaxProvider(context), new ComponentAttributeAnalyzerSyntaxProvider());
}