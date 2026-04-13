using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет синтаксический провайдер для нескольких видов declaration-узлов.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="MemberSyntaxProvider"/>.
/// </remarks>
/// <param name="context">Контекст генератора.</param>
public abstract class MemberSyntaxProvider(IncrementalGeneratorInitializationContext context) : SyntaxProvider<ISymbol, MemberDeclarationSyntax>(context)
{
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Predicate(SyntaxNode node, CancellationToken cancellationToken)
    {
        if (node is not MemberDeclarationSyntax declaration) return false;

        return IsSupportedDeclaration(declaration) && base.Predicate(node, cancellationToken);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ISyntaxProviderInfo<ISymbol, MemberDeclarationSyntax> Transform(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var member = base.Transform(context, cancellationToken);

        if (!TryResolveDeclarationSymbol(context, member.Node, cancellationToken, out var symbol) || symbol is null)
            return default(SyntaxProviderInfo<ISymbol, MemberDeclarationSyntax>);

        if (Attributes.IsEmpty)
        {
            member.Symbol = symbol;
            return member;
        }

        if (TryMatchAttribute(symbol, out var matchedAttribute))
        {
            member.Attribute = matchedAttribute;
            member.Symbol = symbol;
            return member;
        }

        return default(SyntaxProviderInfo<ISymbol, MemberDeclarationSyntax>);
    }

    /// <summary>
    /// Определяет, поддерживается ли declaration провайдером.
    /// </summary>
    /// <param name="declaration">Declaration.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual bool IsSupportedDeclaration(MemberDeclarationSyntax declaration)
        => declaration is TypeDeclarationSyntax or PropertyDeclarationSyntax or FieldDeclarationSyntax or MethodDeclarationSyntax or IndexerDeclarationSyntax or EventDeclarationSyntax or EventFieldDeclarationSyntax;

    /// <summary>
    /// Пытается разрешить symbol для declaration.
    /// </summary>
    /// <param name="context">Контекст генератора.</param>
    /// <param name="node">Declaration-узел.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <param name="symbol">Результирующий symbol.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual bool TryResolveDeclarationSymbol(GeneratorSyntaxContext context, MemberDeclarationSyntax node, CancellationToken cancellationToken, out ISymbol? symbol)
    {
        switch (node)
        {
            case TypeDeclarationSyntax or PropertyDeclarationSyntax or MethodDeclarationSyntax or IndexerDeclarationSyntax or EventDeclarationSyntax:
                return TryGetSymbol(context, node, out symbol, cancellationToken);

            case FieldDeclarationSyntax field:
                foreach (var variable in field.Declaration.Variables)
                {
                    if (TryGetSymbol(context, variable, out symbol, cancellationToken))
                        return true;
                }
                break;

            case EventFieldDeclarationSyntax eventField:
                foreach (var variable in eventField.Declaration.Variables)
                {
                    if (TryGetSymbol(context, variable, out symbol, cancellationToken))
                        return true;
                }
                break;
        }

        symbol = null;
        return false;
    }
}