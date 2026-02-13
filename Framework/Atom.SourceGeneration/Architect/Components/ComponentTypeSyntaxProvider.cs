using System.Collections.Immutable;
using System.Runtime.CompilerServices;
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ComponentTypeSyntaxProvider(IncrementalGeneratorInitializationContext context) : base(context) => WithAttribute();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WithAttribute() => WithAttribute("Component");

    /// <inheritdoc/>
    protected override void OnExecute(SourceProductionContext context, string entityName, ImmutableArray<ISyntaxProviderInfo<ITypeSymbol, TypeDeclarationSyntax>> sources)
    {
        var type = sources[0];
        var ns = type.Symbol?.ContainingNamespace.ToDisplayString();
        var isSealed = sources[0].Symbol?.IsSealed is true;
        var classBuilder = CreateComponentClass(entityName, isSealed);
        var src = BuildSource(ns, classBuilder);
        if (!string.IsNullOrEmpty(src)) context.AddSource($"{entityName}.Component.g.cs", SourceText.From(src, Encoding.UTF8));
    }

    private static ClassEntity CreateComponentClass(string entityName, bool isSealed)
    {
        var builder = ClassEntity.Create(entityName, AccessModifier.Public)
            .AsPartial()
            .WithParent<IComponent>(withNullable: default)
            .WithEvent(CreateComponentEvent("Attached"))
            .WithEvent(CreateComponentEvent("Detached"))
            .WithProperty(CreateOwnerProperty(isSealed))
            .WithProperty(CreateIsAttachedProperty())
            .WithProperty(CreateIsAttachedByOwnerProperty());

        foreach (var method in CreateComponentMethods(isSealed)) builder = builder.WithMethod(method);
        return builder;
    }

    private static EventMember CreateComponentEvent(string name) => EventMember.Create(name, AccessModifier.Public)
        .WithType<MutableEventHandler<object, ComponentEventArgs>>(default, withGenericNullable: default)
        .WithComment(InheritdocComment);

    private static PropertyMember CreateOwnerProperty(bool isSealed) => PropertyMember.Create<IComponentOwner>("Owner", AccessModifier.Public)
        .WithComment(InheritdocComment)
        .WithSetter(PropertyMutatorMember.Create()
            .WithAccessModifier(isSealed ? AccessModifier.Private : AccessModifier.Protected));

    private static PropertyMember CreateIsAttachedProperty() => PropertyMember.CreateWithGetterOnly<bool>("IsAttached", AccessModifier.Public)
        .WithComment(InheritdocComment)
        .WithGetter("Owner is not null");

    private static PropertyMember CreateIsAttachedByOwnerProperty() => PropertyMember.Create<bool>("IsAttachedByOwner", AccessModifier.Public)
        .WithComment(InheritdocComment)
        .WithSetter(PropertyMutatorMember.Create()
            .WithAccessModifier(AccessModifier.Public)
            .AsInit());

    private static IEnumerable<MethodMember> CreateComponentMethods(bool isSealed)
    {
        yield return CreateOnAttachedMethod(isSealed);
        yield return CreateOnDetachedMethod(isSealed);
        yield return CreateAttachToMethod();
        yield return CreateDetachMethod();
    }

    private static MethodMember CreateOnAttachedMethod(bool isSealed) => MethodMember.Create("OnAttached", isSealed ? AccessModifier.Private : AccessModifier.Protected)
        .WithComment("Происходит в момент присоединения компонента к новому владельцу.")
        .AsVirtual(!isSealed)
        .WithArgument(MethodArgumentMember.Create("owner").WithType<IComponentOwner>(withNullable: default).WithComment("Новый владелец компонента."))
        .WithCode("if (Attached.On(this, args => { args.Owner = Owner; args.Component = this; })) Owner = owner");

    private static MethodMember CreateOnDetachedMethod(bool isSealed) => MethodMember.Create("OnDetached", isSealed ? AccessModifier.Private : AccessModifier.Protected)
        .WithComment("Происходит в момент отсоединения компонента от старого владельца.")
        .AsVirtual(!isSealed)
        .WithArgument(MethodArgumentMember.Create("owner").WithType<IComponentOwner>(withNullable: default).WithComment("Старый владелец компонента."))
        .WithCode("if (Detached.On(this, args => { args.Owner = Owner; args.Component = this; })) Owner = default");

    private static MethodMember CreateAttachToMethod() => MethodMember.Create("AttachTo", AccessModifier.Public)
        .WithComment(InheritdocComment)
        .WithArgument(MethodArgumentMember.Create("owner").WithType<IComponentOwner>(withNullable: default))
        .WithCode("""
            if (Owner is not null)
            {
                if (ReferenceEquals(owner, Owner)) throw new InvalidOperationException("Компонент уже присоединён к этому владельцу");
                throw new InvalidOperationException("Компонент уже присоединён к другому владельцу");
            }

            OnAttached(owner);
        """);

    private static MethodMember CreateDetachMethod() => MethodMember.Create("Detach", AccessModifier.Public)
        .WithComment(InheritdocComment)
        .WithCode("""
            if (Owner is null) throw new InvalidOperationException("Компонент уже отсоединён от своего владельца");
            OnDetached(Owner);
        """);

    private static string? BuildSource(string? ns, ClassEntity classBuilder) => SourceBuilder.Create()
        .WithNamespace(ns)
        .WithUsing("Atom.Architect.Components")
        .WithClass(classBuilder)
        .Build(release: true);
}
