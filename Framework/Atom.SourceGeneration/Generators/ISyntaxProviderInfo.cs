using Microsoft.CodeAnalysis;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет информацию о синтаксическом узле провайдера.
/// </summary>
/// <typeparam name="TSymbol">Тип символа.</typeparam>
/// <typeparam name="TSyntaxNode">Тип синтаксического узла.</typeparam>
public interface ISyntaxProviderInfo<TSymbol, out TSyntaxNode> where TSymbol : ISymbol where TSyntaxNode : SyntaxNode
{
    /// <summary>
    /// Атрибут.
    /// </summary>
    string? Attribute { get; set; }

    /// <summary>
    /// Символ.
    /// </summary>
    TSymbol? Symbol { get; set; }

    /// <summary>
    /// Синтаксический узел.
    /// </summary>
    TSyntaxNode Node { get; }
}