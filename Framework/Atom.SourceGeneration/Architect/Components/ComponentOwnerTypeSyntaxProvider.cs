#pragma warning disable CA2263

using System.Collections.Immutable;
using System.Text;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atom.Architect.Components;

/// <summary>
/// Представляет синтаксический провайдер для <see cref="ComponentOwnerAttribute"/>.
/// </summary>
public class ComponentOwnerTypeSyntaxProvider : TypeSyntaxProvider
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ComponentOwnerTypeSyntaxProvider"/>.
    /// </summary>
    /// <param name="context">Контекст генератора.</param>
    public ComponentOwnerTypeSyntaxProvider(IncrementalGeneratorInitializationContext context) : base(context) => WithAttribute("ComponentOwner");

    /// <inheritdoc/>
    protected override void OnExecute(SourceProductionContext context, string entityName, ImmutableArray<ISyntaxProviderInfo<ITypeSymbol, TypeDeclarationSyntax>> sources)
    {
        var type = sources[0];
        var ns = type.Symbol?.ContainingNamespace.ToDisplayString();

        var borderTypes = string.Join("\" or \"", type.Symbol?.GetAttributes()
            .Where(attr => attr.AttributeClass?.Name is nameof(ComponentOwnerAttribute))
            .Select(x => x.GetParameter(nameof(ComponentOwnerAttribute.Type), typeof(Type))?.ToString())
            .Where(x => !string.IsNullOrEmpty(x))!);

        borderTypes = $"typeof(T).FullName is \"{borderTypes}\"";

        var classBuilder = ClassEntity.Create(entityName, AccessModifier.Public)
            .AsPartial()
            .WithParent($"IComponentOwner<{entityName}>")
            .WithField(FieldMember.Create("components")
                .WithType<List<IComponent>>(default, default, default)
                .AsReadOnly()
                .WithValue("[]"))
            .WithMethod(MethodMember.Create("OnComponentDetached", AccessModifier.Private)
                .WithArgument(MethodArgumentMember.Create("args")
                    .WithType<ComponentEventArgs>(withNullable: default)
                    .WithComment("Новый владелец компонента."))
                .WithCode("if (args.Component is not null) components.Remove(args.Component)"))
            .WithMethod(MethodMember.Create("Has", AccessModifier.Public)
                .WithComment(InheritdocComment)
                .WithArgument(MethodArgumentMember.Create("component")
                    .WithType("T"))
                .WithCode("components.Contains(component)")
                .WithGeneric("T", "IComponent")
                .WithType<bool>())
            .WithMethod(MethodMember.Create("Has", AccessModifier.Public)
                .WithComment(InheritdocComment)
                .WithCode("components.Any(x => x is T)")
                .WithGeneric("T", "IComponent")
                .WithType<bool>())
            .WithMethod(MethodMember.Create("TryGet", AccessModifier.Public)
                .WithComment(InheritdocComment)
                .WithArgument(MethodArgumentMember.Create("component")
                    .WithType("T?")
                    .AsOut())
                .WithCode(@"component = (T?)components.FirstOrDefault(x => x is T);
                    return components is null")
                .WithGeneric("T", "IComponent")
                .WithType<bool>())
            .WithMethod(MethodMember.Create("Get", AccessModifier.Public)
                .WithComment(InheritdocComment)
                .WithCode("TryGet<T>(out var component) && component is not null ? component : throw new NotSupportedException(\"Не найдено ни одного подходящего компонента\")")
                .WithGeneric("T", "IComponent")
                .WithType("T"))
            .WithMethod(MethodMember.Create("TryGetAll", AccessModifier.Public)
                .WithComment(InheritdocComment)
                .WithArgument(MethodArgumentMember.Create("components")
                    .WithType("IEnumerable<T>")
                    .AsOut())
                .WithCode(@"components = this.components.OfType<T>();
                    return components.Any()")
                .WithGeneric("T", "IComponent")
                .WithType<bool>())
            .WithMethod(MethodMember.Create("GetAll", AccessModifier.Public)
                .WithComment(InheritdocComment)
                .WithCode("TryGetAll<T>(out var items) ? items : throw new NotSupportedException(\"Не найдено ни одного подходящего компонента\")")
                .WithGeneric("T", "IComponent")
                .WithType("IEnumerable<T>"))
            .WithMethod(MethodMember.Create("Use", AccessModifier.Public)
                .WithComment(InheritdocComment)
                .WithArgument(MethodArgumentMember.Create("component")
                    .WithType("T"))
                .WithCode(@"if (!IsSupported<T>()) throw new NotSupportedException($""Компоненты типа '{typeof(T).Name}' не поддерживаются"");

                    if (component.IsAttached)
                    {
                        if (component.Owner == this) throw new InvalidOperationException(""Компонент уже используется этим владельцем"");
                        throw new InvalidOperationException(""Компонент уже используется другим владельцем"");
                    }

                    component.Detached += OnComponentDetached;
                    component.AttachTo(this);

                    return this")
                .WithGeneric(GenericEntity.Create("T", "IComponent")
                    .WithAttribute("DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)"))
                .WithType(entityName))
            .WithMethod(MethodMember.Create("Use", AccessModifier.Public)
                .WithComment(InheritdocComment)
                .WithCode(@"if (!IsSupported<T>()) throw new NotSupportedException($""Компоненты типа '{typeof(T).Name}' не поддерживаются"");
                    var component = ObjectPool<T>.Shared.Rent(() => new T() { IsAttachedByOwner = true });
                    return Use(component)")
                .WithGeneric(GenericEntity.Create("T", "IComponent", "new()")
                    .WithAttribute("DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)"))
                .WithType(entityName))
            .WithMethod(MethodMember.Create("UnUse", AccessModifier.Public)
                .WithComment(InheritdocComment)
                .WithArgument(MethodArgumentMember.Create("component")
                    .WithType("T"))
                .WithCode(@"if (!component.IsAttached) throw new InvalidOperationException(""Компонент уже был отсоединён"");
                    if (component.Owner != this) throw new InvalidOperationException(""Компонент присоединён к другому владельцу"");

                    component.Detach();
                    component.Detached -= OnComponentDetached;

                    if (component.IsAttachedByOwner)
                    {
                        if (component is IPooled pooled) pooled.Reset();
                        ObjectPool<T>.Shared.Return(component);
                    }

                    return this")
                .WithGeneric(GenericEntity.Create("T", "IComponent")
                    .WithAttribute("DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)"))
                .WithType(entityName))
            .WithMethod(MethodMember.Create("UnUse", AccessModifier.Public)
                .WithComment(InheritdocComment)
                .WithCode(@"if (!TryGetAll<T>(out var items)) throw new InvalidOperationException($""Компоненты типа '{typeof(T).Name}' не найдены"");
                    foreach (var item in items) UnUse(item);
                    return this")
                .WithGeneric(GenericEntity.Create("T", "IComponent")
                    .WithAttribute("DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)"))
                .WithType(entityName))
            .WithMethod(MethodMember.Create("IsSupported", AccessModifier.Public)
                .WithComment(InheritdocComment)
                .WithCode(borderTypes)
                .WithGeneric("T", "IComponent")
                .WithType<bool>());

        var sourceCode = SourceBuilder.Create()
            .WithNamespace(ns)
            .WithUsing("System.Diagnostics.CodeAnalysis",
                "System.Linq",
                "Atom.Buffers",
                "Atom.Architect.Components")
            .WithClass(classBuilder);

        var src = sourceCode.Build(true);
        if (!string.IsNullOrEmpty(src)) context.AddSource($"{entityName}.ComponentOwner.g.cs", SourceText.From(src, Encoding.UTF8));
    }
}