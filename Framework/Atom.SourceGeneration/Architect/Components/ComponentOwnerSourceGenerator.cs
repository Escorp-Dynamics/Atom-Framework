using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;

namespace Atom.Architect.Components;

/// <summary>
/// Генератор кода для <see cref="ComponentOwnerAttribute"/>.
/// </summary>
[Generator]
public class ComponentOwnerSourceGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Инициализирует генератор.
    /// </summary>
    /// <param name="context">Контекст генератора.</param>
    public virtual void Initialize(IncrementalGeneratorInitializationContext context)
        => context.UseProvider(new ComponentOwnerTypeSyntaxProvider(context), new ComponentOwnerAttributeAnalyzerSyntaxProvider());
}