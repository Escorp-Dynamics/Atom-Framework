using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atom.Buffers;

/// <summary>
/// Представляет синтаксический провайдер для <see cref="PooledAttribute"/>.
/// </summary>
public class PooledMethodSyntaxProvider : MethodSyntaxProvider
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="PooledMethodSyntaxProvider"/>.
    /// </summary>
    /// <param name="context">Контекст генератора.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PooledMethodSyntaxProvider(IncrementalGeneratorInitializationContext context) : base(context) => WithAttribute();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WithAttribute() => WithAttribute("Pooled");

    /// <inheritdoc/>
    protected override void OnExecute(SourceProductionContext context, string entityName, ImmutableArray<ISyntaxProviderInfo<IMethodSymbol, MethodDeclarationSyntax>> sources)
    {
        var type = sources[0];
        var ns = type.Symbol?.ContainingNamespace.ToDisplayString();

        var classBuilder = ClassEntity.Create(entityName, AccessModifier.Public)
            .AsPartial()
            .WithParent("IPooled")
            .WithMethod(MethodMember.Create("Rent", AccessModifier.Public)
                .WithComment(InheritdocComment)
                .AsStatic()
                .WithType("T")
                .WithGeneric(GenericEntity.Create("T", "IPooled").WithAttribute("DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)"))
                .WithCode("ObjectPool<T>.Shared.Rent()"))
            .WithMethod(MethodMember.Create("Rent", AccessModifier.Public)
                .WithComment($"Арендует экземпляр <see cref=\"{entityName}\"/> в пуле объектов.")
                .AsStatic()
                .WithType(entityName)
                .WithCode($"Rent<{entityName}>()"))
            .WithMethod(MethodMember.Create("Return", AccessModifier.Public)
                .WithComment(InheritdocComment)
                .AsStatic()
                .WithGeneric(GenericEntity.Create("T", "IPooled").WithAttribute("DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)"))
                .WithArgument(MethodArgumentMember.Create("value").WithType("T"))
                .WithCode("ObjectPool<T>.Shared.Return(value, x => x.Reset())"));

        var sourceCode = SourceBuilder.Create()
            .WithNamespace(ns)
            .WithUsing("System.Diagnostics.CodeAnalysis", "Atom.Buffers")
            .WithClass(classBuilder);

        var src = sourceCode.Build(release: true);
        if (!string.IsNullOrEmpty(src)) context.AddSource($"{entityName}.Pooled.g.cs", SourceText.From(src, Encoding.UTF8));
    }
}
