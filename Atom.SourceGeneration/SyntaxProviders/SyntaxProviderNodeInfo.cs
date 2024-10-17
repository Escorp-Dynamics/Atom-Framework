using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет информацию о синтаксическом узле.
/// </summary>
public record struct SyntaxProviderNodeInfo<TSymbol, TSyntaxNode> where TSymbol : ISymbol where TSyntaxNode : SyntaxNode
{
    /// <summary>
    /// Атрибут.
    /// </summary>
    public string? Attribute { get; internal set; }

    /// <summary>
    /// Символ.
    /// </summary>
    public TSymbol? Symbol { get; internal set; }

    /// <summary>
    /// Синтаксический узел.
    /// </summary>
    public TSyntaxNode Node { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SyntaxProviderNodeInfo{TSymbol, TSyntaxNode}"/>.
    /// </summary>
    /// <param name="node">Синтаксический узел.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SyntaxProviderNodeInfo(TSyntaxNode node) => Node = node;
}