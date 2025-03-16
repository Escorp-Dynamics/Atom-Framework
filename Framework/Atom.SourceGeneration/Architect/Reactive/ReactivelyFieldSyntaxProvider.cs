using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atom.Architect.Reactive;

/// <summary>
/// Представляет синтаксический провайдер для <see cref="ReactivelyAttribute"/>.
/// </summary>
public class ReactivelyFieldSyntaxProvider : FieldSyntaxProvider
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ReactivelyFieldSyntaxProvider"/>.
    /// </summary>
    /// <param name="context">Контекст генератора.</param>
    public ReactivelyFieldSyntaxProvider(IncrementalGeneratorInitializationContext context) : base(context) => WithAttribute("Reactively");

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void OnExecute(SourceProductionContext context, string entityName, ImmutableArray<ISyntaxProviderInfo<IFieldSymbol, FieldDeclarationSyntax>> sources)
    {
        var ns = sources[0].Symbol?.ContainingType.ContainingNamespace.ToDisplayString();
        var isSealed = sources[0].Symbol?.ContainingType.IsSealed is true;

        var classBuilder = ClassEntity.Create(entityName, AccessModifier.Public)
            .AsPartial()
            .AsSealed(isSealed)
            .WithParent<IReactively>(withNullable: default)
            .WithEvent<PropertyChangingEventHandler?>(
                "PropertyChanging",
                AccessModifier.Public,
                "Происходит перед изменением свойства.")
            .WithEvent<PropertyChangedEventHandler?>(
                "PropertyChanged",
                AccessModifier.Public,
                "Происходит после изменения свойства.")
            .WithMethod(MethodMember.Create("SetProperty", isSealed ? AccessModifier.Private : AccessModifier.Protected)
                .WithComment("Изменяет значение свойства.")
                .WithType<bool>()
                .WithGeneric(GenericEntity.Create("T")
                    .WithComment("Тип свойства."))
                .WithArgument(MethodArgumentMember.Create("storage")
                    .WithComment("Ссылка на поле, хранящее значение свойства.")
                    .WithType("T")
                    .AsRef())
                .WithArgument(MethodArgumentMember.Create("value")
                    .WithComment("Значение свойства.")
                    .WithType("T"))
                .WithArgument(MethodArgumentMember.Create("propertyName")
                    .WithComment("Имя свойства.")
                    .WithType<string>()
                    .WithInitialValue("default")
                    .WithAttribute("CallerMemberName"))
                .WithCode(@"if (Equals(storage, value)) return false;

                    OnPropertyChanging(storage, value, propertyName);
                    storage = value;
                    OnPropertyChanged(propertyName);

                    return true;"))
            .WithMethod(MethodMember.Create("OnPropertyChanging", isSealed ? AccessModifier.Private : AccessModifier.Protected)
                .WithComment("Происходит перед изменением свойства.")
                .AsVirtual(!isSealed)
                .WithArgument(MethodArgumentMember.Create<object?>("oldValue")
                    .WithComment("Исходное значение свойства."))
                .WithArgument(MethodArgumentMember.Create<object?>("newValue")
                    .WithComment("Назначаемое значение свойства."))
                .WithArgument(MethodArgumentMember.Create("propertyName")
                    .WithComment("Имя свойства.")
                    .WithAttribute("CallerMemberName")
                    .WithType<string?>()
                    .WithInitialValue("default"))
                .WithCode("PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName))"))
            .WithMethod(MethodMember.Create("OnPropertyChanged", isSealed ? AccessModifier.Private : AccessModifier.Protected)
                .WithComment("Происходит после изменения свойства.")
                .AsVirtual(!isSealed)
                .WithArgument(MethodArgumentMember.Create("propertyName")
                    .WithComment("Имя свойства.")
                    .WithAttribute("CallerMemberName")
                    .WithType<string?>()
                    .WithInitialValue("default"))
                    .WithCode("PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName))"));

        foreach (var field in sources)
        {
            var node = field.Node;

            var type = field.Symbol?.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? node.Declaration.Type.ToString();
            if (!type.EndsWith('?') && field.Symbol?.Type.NullableAnnotation is NullableAnnotation.Annotated) type += "?";

            var comment = string.Empty;
            field.Symbol?.TryParseXmlDocumentation(out comment);
            if (string.IsNullOrEmpty(comment)) comment = "<inheritdoc/>";

            var attribute = field.Symbol?.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name is nameof(ReactivelyAttribute));

            var propertyName = attribute!.GetParameter<string>(nameof(ReactivelyAttribute.PropertyName));
            var accessModifier = attribute.GetParameter<AccessModifier?>(nameof(ReactivelyAttribute.AccessModifier)) ?? AccessModifier.Public;

            if (accessModifier is AccessModifier.Protected or AccessModifier.ProtectedInternal && isSealed) accessModifier = AccessModifier.Private;

            var asVirtual = attribute.GetParameter<bool?>(nameof(ReactivelyAttribute.IsVirtual)) is true && !isSealed;

            foreach (var variable in node.Declaration.Variables)
            {
                var fieldName = variable.Identifier.Text;
                if (string.IsNullOrEmpty(propertyName)) propertyName = ToCamelCase(fieldName.TrimStart('_'));

                classBuilder.WithProperty(PropertyMember.Create()
                    .WithComment(comment)
                    .WithName(propertyName)
                    .WithAccessModifier(accessModifier)
                    .WithType(type)
                    .WithGetter(PropertyAccessorMember.Create()
                        .WithAccessModifier(accessModifier)
                        .WithCode(fieldName)
                    )
                    .WithSetter(PropertyMutatorMember.Create()
                        .WithAccessModifier(accessModifier)
                        .WithCode($"SetProperty(ref {fieldName}, value)")
                    )
                    .AsVirtual(asVirtual)
                );

                classBuilder.WithEvent(EventMember.Create<PropertyChangingEventHandler?>($"{propertyName}Changing", accessModifier)
                    .WithComment($"Происходит в момент изменения свойства <see cref=\"{propertyName}\"/>.")
                    .WithAdder("PropertyChanging += value")
                    .WithRemover("PropertyChanging -= value")
                    .AsVirtual(asVirtual)
                );

                classBuilder.WithEvent(EventMember.Create<PropertyChangedEventHandler?>($"{propertyName}Changed", accessModifier)
                    .WithComment($"Происходит после изменения свойства <see cref=\"{propertyName}\"/>.")
                    .WithAdder("PropertyChanged += value")
                    .WithRemover("PropertyChanged -= value")
                    .AsVirtual(asVirtual)
                );
            }
        }

        var sourceCode = SourceBuilder.Create()
            .WithNamespace(ns)
            .WithUsing("System.ComponentModel",
                "System.Runtime.CompilerServices",
                "Atom.Architect.Reactive")
            .WithClass(classBuilder);

        var src = sourceCode.Build(true);
        if (!string.IsNullOrEmpty(src)) context.AddSource($"{entityName}.Reactively.g.cs", SourceText.From(src, Encoding.UTF8));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ToCamelCase(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName)) return fieldName;

        var firstChar = char.ToUpperInvariant(fieldName[0]);
        return firstChar + fieldName[1..];
    }
}