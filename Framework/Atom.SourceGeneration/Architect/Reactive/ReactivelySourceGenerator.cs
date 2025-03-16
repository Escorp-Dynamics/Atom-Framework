using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;

namespace Atom.Architect.Reactive;

/// <summary>
/// Генератор кода для <see cref="ReactivelyAttribute"/>.
/// </summary>
[Generator]
public class ReactivelySourceGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Инициализирует генератор.
    /// </summary>
    /// <param name="context">Контекст генератора.</param>
    public virtual void Initialize(IncrementalGeneratorInitializationContext context)
        => context.UseProvider(new ReactivelyFieldSyntaxProvider(context), new ReactivelyAttributeAnalyzerSyntaxProvider());
}