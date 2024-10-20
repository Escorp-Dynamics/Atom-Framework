using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;

namespace Atom.Architect.Reactive;

/// <summary>
/// Генератор кода для <see cref="Reactively"/>.
/// </summary>
[Generator]
public class ReactivelySourceGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Инициализирует генератор.
    /// </summary>
    /// <param name="context">контекст генератора.</param>
    public virtual void Initialize(IncrementalGeneratorInitializationContext context)
        => context.UseProvider(new ReactivelyFieldSyntaxProvider(), new ReactivelyAttributeAnalyzerSyntaxProvider());
}