using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет синтаксический провайдер свойств.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="PropertySyntaxProvider"/>.
/// </remarks>
/// <param name="context">Контекст генератора.</param>
public abstract class PropertySyntaxProvider(IncrementalGeneratorInitializationContext context) : SyntaxProvider<IPropertySymbol, PropertyDeclarationSyntax>(context)
{
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ISyntaxProviderInfo<IPropertySymbol, PropertyDeclarationSyntax> Transform(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var member = base.Transform(context, cancellationToken);

        if (TryGetSymbol(context, member.Node, out var s, cancellationToken) && s is IPropertySymbol symbol)
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

        return default(SyntaxProviderInfo<IPropertySymbol, PropertyDeclarationSyntax>);
    }
}