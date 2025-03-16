using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;

namespace Atom.Text.Json;

/// <summary>
/// Генератор кода для <see cref="JsonContextAttribute"/>.
/// </summary>
[Generator]
public class JsonContextSourceGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Инициализирует генератор.
    /// </summary>
    /// <param name="context">Контекст генератора.</param>
    public virtual void Initialize(IncrementalGeneratorInitializationContext context)
        => context.UseProvider(new JsonContextTypeSyntaxProvider(context), new JsonContextAttributeAnalyzerSyntaxProvider());
}