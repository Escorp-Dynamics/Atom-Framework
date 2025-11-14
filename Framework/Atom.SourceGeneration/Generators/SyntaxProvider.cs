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
/// <remarks>
/// Инициализирует новый экземпляр <see cref="AnalyzerSyntaxProvider"/>.
/// </remarks>
/// <param name="context">Контекст генератора.</param>
public abstract class SyntaxProvider<TSymbol, TSyntaxNode>(IncrementalGeneratorInitializationContext context) : ISyntaxProvider<TSymbol, TSyntaxNode> where TSymbol : ISymbol where TSyntaxNode : MemberDeclarationSyntax
{
    /// <summary>
    /// Наследуемая документация.
    /// </summary>
    protected const string InheritdocComment = "<inheritdoc/>";

    private static readonly DiagnosticDescriptor UnhandledException = new(
        "A1000",
        "Необработанное исключение генератора",
        "Генератор вызвал исключение {0}: {1}",
        "Usage",
        DiagnosticSeverity.Error,
isEnabledByDefault: true
    );

    /// <summary>
    /// Используемые атрибуты.
    /// </summary>
    public SparseArray<string> Attributes { get; } = new(128);

    /// <inheritdoc/>
    public IncrementalGeneratorInitializationContext Context { get; protected set; } = context;

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
    protected virtual string? GetEntityName(SyntaxNode? member)
    {
        if (member is null) return default;

        return member switch
        {
            ClassDeclarationSyntax c => c.Identifier.Text,
            StructDeclarationSyntax s => s.Identifier.Text,
            InterfaceDeclarationSyntax i => i.Identifier.Text,
            _ => GetEntityName(member.Parent),
        };
    }

    /// <summary>
    /// Определяет, соответствует ли атрибут.
    /// </summary>
    /// <param name="symbol">Символ.</param>
    /// <param name="matchedAttribute">Искомый атрибут.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected bool TryMatchAttribute(TSymbol symbol, out string? matchedAttribute)
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
            var members = new Dictionary<string, List<ISyntaxProviderInfo<TSymbol, TSyntaxNode>>>(StringComparer.Ordinal);

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
            ReportExceptionDiagnostic(context, ex, e => CreateExceptionDiagnostic(e, location: null));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Diagnostic CreateExceptionDiagnostic(Exception exception, Location? location)
        => Diagnostic.Create(UnhandledException, location, exception?.GetType(), exception?.Message);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReportExceptionDiagnostic(SourceProductionContext context, Exception exception, Func<Exception, Diagnostic> diagnosticFactory)
    {
        var diagnostic = diagnosticFactory(exception);
        context.ReportDiagnostic(diagnostic);

        var exceptionInfo = "#error " + exception.ToString().Replace("\n", "\n//");
        context.AddSource("!" + diagnostic.Descriptor.Id + "-" + Guid.NewGuid(), exceptionInfo);
    }

    /// <summary>
    /// Пытается получить символ.
    /// </summary>
    /// <param name="context">Контекст генератора.</param>
    /// <param name="variable"></param>
    /// <param name="symbol"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool TryGetSymbol(GeneratorSyntaxContext context, SyntaxNode variable, out ISymbol? symbol, CancellationToken cancellationToken)
    {
        symbol = context.SemanticModel.GetDeclaredSymbol(variable, cancellationToken);
        return symbol is not null;
    }

    /// <summary>
    /// Определяет, есть ли реализация в типе.
    /// </summary>
    /// <param name="compilation">Компиляция.</param>
    /// <param name="type">Тип.</param>
    /// <param name="memberName">Имя члена.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool HasBaseImplementation([NotNull] Compilation compilation, INamedTypeSymbol? type, string memberName)
    {
        var currentType = type;

        while (currentType is not null)
        {
            if (currentType.GetMembers(memberName).Length > 0) return true;
            currentType = currentType.BaseType;
        }

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var root = syntaxTree.GetRoot();
            if (root.DescendantNodes().Any(node => node is MethodDeclarationSyntax method && string.Equals(method.Identifier.Text, memberName, StringComparison.Ordinal))) return true;
        }

        return default;
    }
}