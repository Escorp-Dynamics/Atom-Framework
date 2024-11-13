#pragma warning disable RS2008

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
    private static readonly DiagnosticDescriptor UnhandledException = new(
        "A1000",
        "Необработанное исключение генератора",
        "Генератор вызвал исключение {0}: {1}",
        "Usage",
        DiagnosticSeverity.Error,
        true
    );

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
    protected abstract void OnExecute(SourceProductionContext context, string entityName, ImmutableArray<ISyntaxProviderInfo<TSymbol, TSyntaxNode>> sources);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual bool Predicate(SyntaxNode node, CancellationToken cancellationToken)
    {
        if (node is not TSyntaxNode member) return default;
        if (Attributes.IsEmpty) return true;
        if (member.AttributeLists.Count is 0) return default;

        foreach (var attributeLists in member.AttributeLists)
        {
            foreach (var attribute in attributeLists.Attributes)
            {
                var attr = attribute.Name.ToString();
                if (!string.IsNullOrEmpty(attr) && Attributes.Any(x => attr.Equals(x, StringComparison.InvariantCultureIgnoreCase))) return true;
            }
        }

        return default;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual ISyntaxProviderInfo<TSymbol, TSyntaxNode> Transform(GeneratorSyntaxContext context, CancellationToken cancellationToken)
        => new SyntaxProviderInfo<TSymbol, TSyntaxNode>((TSyntaxNode)context.Node);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Execute(SourceProductionContext context, ImmutableArray<ISyntaxProviderInfo<TSymbol, TSyntaxNode>> sources)
    {
        try
        {
            var members = new Dictionary<string, List<ISyntaxProviderInfo<TSymbol, TSyntaxNode>>>();

            foreach (var target in sources)
            {
                var entityName = GetEntityName(target.Node);
                if (string.IsNullOrEmpty(entityName)) continue;

                if (!members.TryGetValue(entityName, out var value))
                {
                    value = [];
                    members[entityName] = value;
                }

                value.Add(target);
            }

            foreach (var kv in members)
            {
                if (kv.Value.Count is 0) continue;
                OnExecute(context, kv.Key, [.. kv.Value]);
            }
        }
        catch (Exception ex)
        {
            ReportExceptionDiagnostic(context, ex, e => CreateExceptionDiagnostic(e, null));
        }
    }

    private static Diagnostic CreateExceptionDiagnostic(Exception exception, Location? location)
        => Diagnostic.Create(UnhandledException, location, exception?.GetType(), exception?.Message);

    private static void ReportExceptionDiagnostic(SourceProductionContext context, Exception exception, Func<Exception, Diagnostic> diagnosticFactory)
    {
        var diagnostic = diagnosticFactory(exception);
        context.ReportDiagnostic(diagnostic);

        var exceptionInfo = "#error " + exception.ToString().Replace("\n", "\n//");
        context.AddSource("!" + diagnostic.Descriptor.Id + "-" + Guid.NewGuid(), exceptionInfo);
    }
}