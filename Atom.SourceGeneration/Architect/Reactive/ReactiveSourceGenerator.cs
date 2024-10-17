using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;

namespace Atom.Architect.Reactive;

/// <summary>
/// Генератор кода для <see cref="Reactive"/>.
/// </summary>
[Generator]
public class ReactiveSourceGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Инициализирует генератор кода.
    /// </summary>
    /// <param name="context">Контекст инициализации генератора.</param>/// 
    public void Initialize(IncrementalGeneratorInitializationContext context) => context.UseProvider(new ReactiveFieldSyntaxProvider());
}