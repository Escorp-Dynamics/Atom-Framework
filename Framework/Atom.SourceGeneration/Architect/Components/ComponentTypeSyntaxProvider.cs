using System.Collections.Immutable;
using System.Text;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atom.Architect.Components;

/// <summary>
/// Представляет синтаксический провайдер для <see cref="ComponentAttribute"/>.
/// </summary>
public class ComponentTypeSyntaxProvider : TypeSyntaxProvider
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ComponentOwnerTypeSyntaxProvider"/>.
    /// </summary>
    /// <param name="context">Контекст генератора.</param>
    public ComponentTypeSyntaxProvider(IncrementalGeneratorInitializationContext context) : base(context) => WithAttribute("Component");

    /// <inheritdoc/>
    protected override void OnExecute(SourceProductionContext context, string entityName, ImmutableArray<ISyntaxProviderInfo<ITypeSymbol, TypeDeclarationSyntax>> sources)
    {
        var type = sources[0];
        var ns = type.Symbol?.ContainingNamespace.ToDisplayString();
        var isSealed = sources[0].Symbol?.IsSealed is true;

        var classBuilder = ClassEntity.Create(entityName, AccessModifier.Public)
            .AsPartial()
            .WithParent<IComponent>(withNullable: default)
            .WithEvent(EventMember.Create("Attached", AccessModifier.Public)
                .WithType<MutableEventHandler<ComponentEventArgs>>(default, withGenericNullable: default)
                .WithComment(InheritdocComment))
            .WithEvent(EventMember.Create("Detached", AccessModifier.Public)
                .WithType<MutableEventHandler<ComponentEventArgs>>(default, withGenericNullable: default)
                .WithComment(InheritdocComment))
            .WithProperty(PropertyMember.Create<IComponentOwner>("Owner", AccessModifier.Public)
                .WithComment(InheritdocComment)
                .WithSetter(PropertyMutatorMember.Create()
                    .WithAccessModifier(isSealed ? AccessModifier.Private : AccessModifier.Protected)))
            .WithProperty(PropertyMember.CreateWithGetterOnly<bool>("IsAttached", AccessModifier.Public)
                .WithComment(InheritdocComment)
                .WithGetter("Owner is not null"))
            .WithProperty(PropertyMember.Create<bool>("IsAttachedByOwner", AccessModifier.Public)
                .WithComment(InheritdocComment)
                .WithSetter(PropertyMutatorMember.Create()
                    .WithAccessModifier(AccessModifier.Public)
                    .AsInit()))
            .WithMethod(MethodMember.Create("OnAttached", isSealed ? AccessModifier.Private : AccessModifier.Protected)
                .WithComment("Происходит в момент присоединения компонента к новому владельцу.")
                .AsVirtual(!isSealed)
                .WithArgument(MethodArgumentMember.Create("owner")
                    .WithType<IComponentOwner>(withNullable: default)
                    .WithComment("Новый владелец компонента."))
                .WithCode("if (Attached.On(args => { args.Owner = Owner; args.Component = this; })) Owner = owner"))
            .WithMethod(MethodMember.Create("OnDetached", isSealed ? AccessModifier.Private : AccessModifier.Protected)
                .WithComment("Происходит в момент отсоединения компонента от старого владельца.")
                .AsVirtual(!isSealed)
                .WithArgument(MethodArgumentMember.Create("owner")
                    .WithType<IComponentOwner>(withNullable: default)
                    .WithComment("Старый владелец компонента."))
                .WithCode("if (Detached.On(args => { args.Owner = Owner; args.Component = this; })) Owner = default"))
            .WithMethod(MethodMember.Create("AttachTo", AccessModifier.Public)
                .WithComment(InheritdocComment)
                .WithArgument(MethodArgumentMember.Create("owner")
                    .WithType<IComponentOwner>(withNullable: default))
                .WithCode(@"if (Owner is not null)
                    {
                        if (ReferenceEquals(owner, Owner)) throw new InvalidOperationException(""Компонент уже присоединён к этому владельцу"");
                        throw new InvalidOperationException(""Компонент уже присоединён к другому владельцу"");
                    }

                    OnAttached(owner);"))
            .WithMethod(MethodMember.Create("Detach", AccessModifier.Public)
                .WithComment(InheritdocComment)
                .WithCode(@"if (Owner is null) throw new InvalidOperationException(""Компонент уже отсоединён от своего владельца"");
                    OnDetached(Owner);"));

        var sourceCode = SourceBuilder.Create()
            .WithNamespace(ns)
            .WithUsing("Atom.Architect.Components")
            .WithClass(classBuilder);

        var src = sourceCode.Build(true);
        if (!string.IsNullOrEmpty(src)) context.AddSource($"{entityName}.Component.g.cs", SourceText.From(src, Encoding.UTF8));
    }
}