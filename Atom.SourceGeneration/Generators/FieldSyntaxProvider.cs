using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет синтаксический провайдер полей.
/// </summary>
public abstract class FieldSyntaxProvider : SyntaxProvider<IFieldSymbol, FieldDeclarationSyntax>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryMatchAttribute(IFieldSymbol symbol, out string? matchedAttribute)
    {
        var attributes = symbol.GetAttributes();
        matchedAttribute = default;

        foreach (var attribute in attributes)
        {
            var attrName = attribute.AttributeClass?.Name;
            if (string.IsNullOrEmpty(attrName)) continue;

            foreach (var sourceAttribute in Attributes)
            {
                if (!attrName.Equals($"{sourceAttribute}Attribute", StringComparison.InvariantCultureIgnoreCase)) continue;

                matchedAttribute = sourceAttribute;
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ISyntaxProviderInfo<IFieldSymbol, FieldDeclarationSyntax> Transform(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var member = base.Transform(context, cancellationToken);
        var field = member.Node;

        foreach (var variable in field.Declaration.Variables)
        {
            if (!TryGetFieldSymbol(context, variable, cancellationToken, out var symbol) || symbol is null) continue;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetFieldSymbol(GeneratorSyntaxContext context, VariableDeclaratorSyntax variable, CancellationToken cancellationToken, out IFieldSymbol? symbol)
    {
        symbol = context.SemanticModel.GetDeclaredSymbol(variable, cancellationToken) as IFieldSymbol;
        return symbol is not null;
    }
}