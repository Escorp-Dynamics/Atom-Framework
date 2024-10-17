using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет расширения генератора.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Добавляет синтаксический провайдер к контексту инкрементального генератора.
    /// </summary>
    /// <param name="context">Контекст инкрементального генератора.</param>
    /// <param name="provider">синтаксический провайдер.</param>
    /// <typeparam name="TSymbol">Тип символа.</typeparam>
    /// <typeparam name="TSyntaxNode">Тип синтаксического узла провайдера.</typeparam>
    public static IncrementalGeneratorInitializationContext UseProvider<TSymbol, TSyntaxNode>(this IncrementalGeneratorInitializationContext context,
        [NotNull] ISyntaxProvider<TSymbol, TSyntaxNode> provider)
        where TSymbol : ISymbol where TSyntaxNode : SyntaxNode
    {
        var syntaxProvider = context.SyntaxProvider
            .CreateSyntaxProvider(provider.Predicate, provider.Transform)
            .Where(static m => m != default);

        context.RegisterSourceOutput(syntaxProvider.Collect(), provider.Execute);
        return context;
    }
}