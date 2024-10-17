using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Atom.Collections;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет базовую реализацию синтаксического провайдера.
/// </summary>
/// <typeparam name="TSymbol">Тип символа.</typeparam>
/// <typeparam name="TSyntaxNode">Тип синтаксического узла провайдера.</typeparam>
public abstract class SyntaxProvider<TSymbol, TSyntaxNode> : ISyntaxProvider<TSymbol, TSyntaxNode> where TSymbol : ISymbol where TSyntaxNode : MemberDeclarationSyntax
{
    /// <summary>
    /// Используемые атрибуты.
    /// </summary>
    public SparseArray<string> Attributes { get; } = new(128);

    /// <summary>
    /// Добавляет атрибут.
    /// </summary>
    /// <param name="attribute">Атрибут.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void WithAttribute(string attribute) => Attributes.Add(attribute);

    /// <summary>
    /// Возвращает имя сущности по контексту.
    /// </summary>
    /// <param name="member">Член сущности.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual string? GetEntityName([NotNull] TSyntaxNode member) => member.Parent switch
    {
        ClassDeclarationSyntax c => c.Identifier.Text,
        StructDeclarationSyntax s => s.Identifier.Text,
        InterfaceDeclarationSyntax i => i.Identifier.Text,
        _ => default,
    };

    /// <summary>
    /// Происходит в момент генерации кода.
    /// </summary>
    /// <param name="context">Контекст генерации.</param>
    /// <param name="entityName">Имя сущности.</param>
    /// <param name="sources">Источники генерации.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected abstract void OnExecute(SourceProductionContext context, string entityName, ImmutableArray<SyntaxProviderNodeInfo<TSymbol, TSyntaxNode>> sources);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual bool Predicate(SyntaxNode node, CancellationToken cancellationToken)
    {
        if (node is not MemberDeclarationSyntax member) return default;
        if (Attributes.IsEmpty) return true;
        if (member.AttributeLists.Count is 0) return default;

        foreach (var attributeLists in member.AttributeLists)
            foreach (var attribute in attributeLists.Attributes)
            {
                var attr = attribute.Name.ToString();
                if (string.IsNullOrEmpty(attr)) continue;
                if (Attributes.Any(x => attr.Equals(x, StringComparison.InvariantCultureIgnoreCase))) return true;
            }

        return default;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual SyntaxProviderNodeInfo<TSymbol, TSyntaxNode> Transform(GeneratorSyntaxContext context, CancellationToken cancellationToken)
        => new((TSyntaxNode)context.Node);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Execute(SourceProductionContext context, ImmutableArray<SyntaxProviderNodeInfo<TSymbol, TSyntaxNode>> sources)
    {
        var members = new Dictionary<string, List<SyntaxProviderNodeInfo<TSymbol, TSyntaxNode>>>();

        foreach (var target in sources)
        {
            var entityName = GetEntityName(target.Node);
            if (string.IsNullOrEmpty(entityName)) continue;

            if (!members.TryGetValue(entityName, out var value))
            {
                value = [];
                members[entityName] = [];
            }

            value.Add(target);
        }

        foreach (var kv in members)
        {
            if (kv.Value.Count is 0) continue;
            OnExecute(context, kv.Key, [.. kv.Value]);
        }
    }
}