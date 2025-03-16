using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет синтаксический провайдер полей.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="FieldSyntaxProvider"/>.
/// </remarks>
/// <param name="context">Контекст генератора.</param>
public abstract class FieldSyntaxProvider(IncrementalGeneratorInitializationContext context) : SyntaxProvider<IFieldSymbol, FieldDeclarationSyntax>(context)
{
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ISyntaxProviderInfo<IFieldSymbol, FieldDeclarationSyntax> Transform(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var member = base.Transform(context, cancellationToken);
        var field = member.Node;

        foreach (var variable in field.Declaration.Variables)
        {
            if (!TryGetSymbol(context, variable, out var s, cancellationToken) || s is not IFieldSymbol symbol) continue;

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

        return default(SyntaxProviderInfo<IFieldSymbol, FieldDeclarationSyntax>);
    }
}