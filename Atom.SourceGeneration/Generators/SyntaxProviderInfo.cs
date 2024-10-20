using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет информацию о синтаксическом узле.
/// </summary>
public record struct SyntaxProviderInfo<TSymbol, TSyntaxNode> : ISyntaxProviderInfo<TSymbol, TSyntaxNode> where TSymbol : ISymbol where TSyntaxNode : SyntaxNode
{
    /// <summary>
    /// Атрибут.
    /// </summary>
    public string? Attribute { get; set; }

    /// <summary>
    /// Символ.
    /// </summary>
    public TSymbol? Symbol { get; set; }

    /// <summary>
    /// Синтаксический узел.
    /// </summary>
    public TSyntaxNode Node { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SyntaxProviderInfo{TSymbol, TSyntaxNode}"/>.
    /// </summary>
    /// <param name="node">Синтаксический узел.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SyntaxProviderInfo(TSyntaxNode node) => Node = node;
}