#pragma warning disable CA2263

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ComponentOwnerTypeSyntaxProvider(IncrementalGeneratorInitializationContext context) : base(context) => WithAttribute();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WithAttribute() => WithAttribute("ComponentOwner");

    /// <inheritdoc/>
    protected override void OnExecute(SourceProductionContext context, string entityName, ImmutableArray<ISyntaxProviderInfo<ITypeSymbol, TypeDeclarationSyntax>> sources)
    {
        var type = sources[0];
        var ns = type.Symbol?.ContainingNamespace.ToDisplayString();
        var supportedCondition = BuildSupportedCondition(type.Symbol);
        var classBuilder = CreateOwnerClass(entityName, supportedCondition);
        var src = BuildSource(ns, classBuilder);
        if (!string.IsNullOrEmpty(src)) context.AddSource($"{entityName}.ComponentOwner.g.cs", SourceText.From(src, Encoding.UTF8));
    }

    private static string BuildSupportedCondition(ITypeSymbol? symbol)
    {
        if (symbol is null) return "true";

        var matches = symbol.GetAttributes()
            .Where(static attr => attr.AttributeClass?.Name is nameof(ComponentOwnerAttribute))
            .Select(static attr => attr.GetParameter(nameof(ComponentOwnerAttribute.Type), typeof(Type))?.ToString())
            .Where(static x => !string.IsNullOrEmpty(x))
            .ToArray();

        return matches.Length is 0
            ? "true"
            : string.Join(" || ", matches.Select(static match => $"typeof({match}).IsAssignableFrom(typeof(T))"));
    }

    private static ClassEntity CreateOwnerClass(string entityName, string supportedCondition)
    {
        var builder = ClassEntity.Create(entityName, AccessModifier.Public)
            .AsPartial()
            .WithParent($"IComponentOwner<{entityName}>")
            .WithField(CreateComponentsField());

        foreach (var method in CreateOwnerMethods(entityName, supportedCondition))
        {
            builder = builder.WithMethod(method);
        }

        return builder;
    }

    private static FieldMember CreateComponentsField() => FieldMember.Create("components")
        .WithType<List<IComponent>>(default, default, default)
        .AsReadOnly()
        .WithValue("[]");

    private static IEnumerable<MethodMember> CreateOwnerMethods(string entityName, string supportedCondition)
    {
        yield return CreateOnComponentDetachedMethod();
        yield return CreateHasSpecificMethod();
        yield return CreateHasAnyMethod();
        yield return CreateTryGetMethod();
        yield return CreateGetMethod();
        yield return CreateTryGetAllMethod();
        yield return CreateGetAllMethod();
        yield return CreateUseExistingMethod(entityName);
        yield return CreateUseFromPoolMethod(entityName);
        yield return CreateUnUseSpecificMethod(entityName);
        yield return CreateUnUseAllMethod(entityName);

        yield return CreateIsSupportedMethod(supportedCondition);
    }

    private static MethodMember CreateOnComponentDetachedMethod() => MethodMember.Create("OnComponentDetached", AccessModifier.Private)
        .WithArgument(MethodArgumentMember.Create("sender")
            .WithComment("Источник события"))
        .WithArgument(MethodArgumentMember.Create("args")
            .WithType<ComponentEventArgs>(withNullable: default)
            .WithComment("Новый владелец компонента"))
        .WithCode("""
            if (sender is IComponent component) component.Detached -= OnComponentDetached;
            if (args.Component is not null) components.Remove(args.Component);
        """);

    private static MethodMember CreateHasSpecificMethod() => MethodMember.Create("Has", AccessModifier.Public)
        .WithComment(InheritdocComment)
        .WithArgument(MethodArgumentMember.Create("component").WithType("T"))
        .WithCode("components.Contains(component)")
        .WithGeneric("T", "IComponent")
        .WithType<bool>();

    private static MethodMember CreateHasAnyMethod() => MethodMember.Create("Has", AccessModifier.Public)
        .WithComment(InheritdocComment)
        .WithCode("components.Any(x => x is T)")
        .WithGeneric("T", "IComponent")
        .WithType<bool>();

    private static MethodMember CreateTryGetMethod() => MethodMember.Create("TryGet", AccessModifier.Public)
        .WithComment(InheritdocComment)
        .WithArgument(MethodArgumentMember.Create("component").WithType("T?").AsOut())
        .WithCode(@"component = (T?)components.FirstOrDefault(x => x is T);
            return component is not null")
        .WithGeneric("T", "IComponent")
        .WithType<bool>();

    private static MethodMember CreateGetMethod() => MethodMember.Create("Get", AccessModifier.Public)
        .WithComment(InheritdocComment)
        .WithCode("TryGet<T>(out var component) && component is not null ? component : throw new NotSupportedException(\"Не найдено ни одного подходящего компонента\")")
        .WithGeneric("T", "IComponent")
        .WithType("T");

    private static MethodMember CreateTryGetAllMethod() => MethodMember.Create("TryGetAll", AccessModifier.Public)
        .WithComment(InheritdocComment)
        .WithArgument(MethodArgumentMember.Create("components").WithType("IEnumerable<T>").AsOut())
        .WithCode(@"components = this.components.OfType<T>();
            return components.Any()")
        .WithGeneric("T", "IComponent")
        .WithType<bool>();

    private static MethodMember CreateGetAllMethod() => MethodMember.Create("GetAll", AccessModifier.Public)
        .WithComment(InheritdocComment)
        .WithCode("TryGetAll<T>(out var items) ? items : throw new NotSupportedException(\"Не найдено ни одного подходящего компонента\")")
        .WithGeneric("T", "IComponent")
        .WithType("IEnumerable<T>");

    private static MethodMember CreateIsSupportedMethod(string supportedCondition) => MethodMember.Create("IsSupported", AccessModifier.Public)
        .WithComment(InheritdocComment)
        .WithCode(supportedCondition)
        .WithGeneric("T", "IComponent")
        .WithType<bool>();

    private static MethodMember CreateUseExistingMethod(string entityName) => MethodMember.Create("Use", AccessModifier.Public)
        .WithComment(InheritdocComment)
        .WithArgument(MethodArgumentMember.Create("component").WithType("T"))
        .WithCode("""
            if (!IsSupported<T>()) throw new NotSupportedException($"Компоненты типа '{typeof(T).Name}' не поддерживаются");

            if (component.IsAttached)
            {
                if (component.Owner == this) throw new InvalidOperationException("Компонент уже используется этим владельцем");
                throw new InvalidOperationException("Компонент уже используется другим владельцем");
            }

            component.Detached += OnComponentDetached;
            component.AttachTo(this);
            components.Add(component);

            return this
        """)
        .WithGeneric(GenericEntity.Create("T", "IComponent")
            .WithAttribute("DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)"))
        .WithType(entityName);

    private static MethodMember CreateUseFromPoolMethod(string entityName) => MethodMember.Create("Use", AccessModifier.Public)
        .WithComment(InheritdocComment)
        .WithCode("""
            if (!IsSupported<T>()) throw new NotSupportedException($"Компоненты типа '{typeof(T).Name}' не поддерживаются");
            var component = ObjectPool<T>.Shared.Rent(() => new T() { IsAttachedByOwner = true });
            return Use(component)
        """)
        .WithGeneric(GenericEntity.Create("T", "IComponent", "new()")
            .WithAttribute("DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)"))
        .WithType(entityName);

    private static MethodMember CreateUnUseSpecificMethod(string entityName) => MethodMember.Create("UnUse", AccessModifier.Public)
        .WithComment(InheritdocComment)
        .WithArgument(MethodArgumentMember.Create("component").WithType("T"))
        .WithCode("""
            if (!component.IsAttached) throw new InvalidOperationException("Компонент уже был отсоединён");
            if (component.Owner != this) throw new InvalidOperationException("Компонент присоединён к другому владельцу");

            component.Detach();
            component.Detached -= OnComponentDetached;

            if (component.IsAttachedByOwner)
            {
                if (component is IPooled pooled) pooled.Reset();
                ObjectPool<T>.Shared.Return(component);
            }

            return this
        """)
        .WithGeneric(GenericEntity.Create("T", "IComponent")
            .WithAttribute("DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)"))
        .WithType(entityName);

    private static MethodMember CreateUnUseAllMethod(string entityName) => MethodMember.Create("UnUse", AccessModifier.Public)
        .WithComment(InheritdocComment)
        .WithCode("""
            if (!TryGetAll<T>(out var items)) throw new InvalidOperationException($"Компоненты типа '{typeof(T).Name}' не найдены");
            foreach (var item in items) UnUse(item);
            return this
        """)
        .WithGeneric(GenericEntity.Create("T", "IComponent")
            .WithAttribute("DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)"))
        .WithType(entityName);

    private static string? BuildSource(string? ns, ClassEntity classBuilder) => SourceBuilder.Create()
        .WithNamespace(ns)
        .WithUsing("System.Diagnostics.CodeAnalysis", "System.Linq", "Atom.Buffers", "Atom.Architect.Components")
        .WithClass(classBuilder)
        .Build(release: true);
}
