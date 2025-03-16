using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет синтаксический провайдер методов.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="MethodSyntaxProvider"/>.
/// </remarks>
/// <param name="context">Контекст генератора.</param>
public abstract class MethodSyntaxProvider(IncrementalGeneratorInitializationContext context) : SyntaxProvider<IMethodSymbol, MethodDeclarationSyntax>(context)
{
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ISyntaxProviderInfo<IMethodSymbol, MethodDeclarationSyntax> Transform(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var member = base.Transform(context, cancellationToken);

        if (TryGetSymbol(context, member.Node, out var s, cancellationToken) && s is IMethodSymbol symbol)
        {
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
        }

        return default(SyntaxProviderInfo<IMethodSymbol, MethodDeclarationSyntax>);
    }
}