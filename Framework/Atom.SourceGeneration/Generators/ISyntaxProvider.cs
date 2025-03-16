using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет базовый интерфейс для реализации синтаксических провайдеров.
/// </summary>
/// <typeparam name="TSymbol">Тип символа.</typeparam>
/// <typeparam name="TSyntaxNode">Тип синтаксического узла провайдера.</typeparam>
public interface ISyntaxProvider<TSymbol, TSyntaxNode> where TSymbol : ISymbol where TSyntaxNode : SyntaxNode
{
    /// <summary>
    /// Контекст генератора.
    /// </summary>
    IncrementalGeneratorInitializationContext Context { get; }

    /// <summary>
    /// Предикат для определения нужных синтаксических конструкций при генерации.
    /// </summary>
    /// <param name="node">Синтаксический узел.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    bool Predicate(SyntaxNode node, CancellationToken cancellationToken);

    /// <summary>
    /// Преобразует синтаксический контекст в объект типа T.
    /// </summary>
    /// <param name="context">Контекст генерации кода.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ISyntaxProviderInfo<TSymbol, TSyntaxNode> Transform(GeneratorSyntaxContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Выполняет генерацию кода.
    /// </summary>
    /// <param name="context">Контекст генерации.</param>
    /// <param name="sources">Доступные источники.</param>
    void Execute(SourceProductionContext context, ImmutableArray<ISyntaxProviderInfo<TSymbol, TSyntaxNode>> sources);
}