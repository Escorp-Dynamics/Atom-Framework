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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReactivelyFieldSyntaxProvider(IncrementalGeneratorInitializationContext context) : base(context) => WithAttribute();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WithAttribute() => WithAttribute("Reactively");

    /// <inheritdoc/>
    protected override void OnExecute(SourceProductionContext context, string entityName, ImmutableArray<ISyntaxProviderInfo<IFieldSymbol, FieldDeclarationSyntax>> sources)
    {
        var ns = sources[0].Symbol?.ContainingType.ContainingNamespace.ToDisplayString();
        var isSealed = sources[0].Symbol?.ContainingType.IsSealed is true;
        var classBuilder = CreateReactiveClass(entityName, isSealed);
        classBuilder = AddReactiveMembers(classBuilder, sources, isSealed);
        var src = BuildSource(ns, classBuilder);
        if (!string.IsNullOrEmpty(src)) context.AddSource($"{entityName}.Reactively.g.cs", SourceText.From(src, Encoding.UTF8));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ToCamelCase(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName)) return fieldName;

        var firstChar = char.ToUpperInvariant(fieldName[0]);
        return firstChar + fieldName[1..];
    }

    private static ClassEntity CreateReactiveClass(string entityName, bool isSealed) => ClassEntity.Create(entityName, AccessModifier.Public)
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
        .WithMethod(CreateSetPropertyMethod(isSealed))
        .WithMethod(CreateOnPropertyChangingMethod(isSealed))
        .WithMethod(CreateOnPropertyChangedMethod(isSealed));

    private static MethodMember CreateSetPropertyMethod(bool isSealed) => MethodMember.Create("SetProperty", isSealed ? AccessModifier.Private : AccessModifier.Protected)
        .WithComment("Изменяет значение свойства.")
        .WithType<bool>()
        .WithGeneric(GenericEntity.Create("T").WithComment("Тип свойства."))
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

            return true;");

    private static MethodMember CreateOnPropertyChangingMethod(bool isSealed) => MethodMember.Create("OnPropertyChanging", isSealed ? AccessModifier.Private : AccessModifier.Protected)
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
        .WithCode("PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName))");

    private static MethodMember CreateOnPropertyChangedMethod(bool isSealed) => MethodMember.Create("OnPropertyChanged", isSealed ? AccessModifier.Private : AccessModifier.Protected)
        .WithComment("Происходит после изменения свойства.")
        .AsVirtual(!isSealed)
        .WithArgument(MethodArgumentMember.Create("propertyName")
            .WithComment("Имя свойства.")
            .WithAttribute("CallerMemberName")
            .WithType<string?>()
            .WithInitialValue("default"))
        .WithCode("PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName))");

    private static ClassEntity AddReactiveMembers(ClassEntity builder, ImmutableArray<ISyntaxProviderInfo<IFieldSymbol, FieldDeclarationSyntax>> sources, bool isSealed)
    {
        foreach (var field in sources)
        {
            builder = ProcessField(builder, field, isSealed);
        }

        return builder;
    }

    private static ClassEntity ProcessField(ClassEntity builder, ISyntaxProviderInfo<IFieldSymbol, FieldDeclarationSyntax> fieldInfo, bool isSealed)
    {
        var node = fieldInfo.Node;
        var fieldType = GetFieldType(fieldInfo);
        var comment = GetDocumentationComment(fieldInfo.Symbol);
        var attribute = GetReactivelyAttribute(fieldInfo.Symbol);
        if (attribute is null) return builder;

        var propertyNameOverride = attribute.GetParameter<string>(nameof(ReactivelyAttribute.PropertyName));
        var accessModifier = DetermineAccessModifier(attribute, isSealed);
        var isVirtual = attribute.GetParameter<bool?>(nameof(ReactivelyAttribute.IsVirtual)) is true && !isSealed;

        foreach (var variable in node.Declaration.Variables)
        {
            var fieldName = variable.Identifier.Text;
            var propertyName = DeterminePropertyName(propertyNameOverride, fieldName);

            builder = builder
                .WithProperty(CreateReactiveProperty(propertyName, fieldName, fieldType, comment, accessModifier, isVirtual))
                .WithEvent(CreatePropertyChangingEvent(propertyName, accessModifier, isVirtual))
                .WithEvent(CreatePropertyChangedEvent(propertyName, accessModifier, isVirtual));
        }

        return builder;
    }

    private static string GetFieldType(ISyntaxProviderInfo<IFieldSymbol, FieldDeclarationSyntax> fieldInfo)
    {
        var symbolType = fieldInfo.Symbol?.Type;
        var declaredType = fieldInfo.Node.Declaration.Type.ToString();
        var typeName = symbolType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? declaredType;
        if (!typeName.EndsWith('?') && symbolType?.NullableAnnotation is NullableAnnotation.Annotated) typeName += "?";
        return typeName;
    }

    private static string GetDocumentationComment(IFieldSymbol? symbol)
    {
        if (symbol is null) return "<inheritdoc/>";
        symbol.TryParseXmlDocumentation(out var comment);
        if (string.IsNullOrEmpty(comment)) return "<inheritdoc/>";
        return comment;
    }

    private static AttributeData? GetReactivelyAttribute(IFieldSymbol? symbol) => symbol?.GetAttributes()
        .FirstOrDefault(attr => attr.AttributeClass?.Name is nameof(ReactivelyAttribute));

    private static AccessModifier DetermineAccessModifier(AttributeData attribute, bool isSealed)
    {
        var modifier = attribute.GetParameter<AccessModifier?>(nameof(ReactivelyAttribute.AccessModifier)) ?? AccessModifier.Public;
        if (modifier is AccessModifier.Protected or AccessModifier.ProtectedInternal && isSealed) return AccessModifier.Private;
        return modifier;
    }

    private static string DeterminePropertyName(string? propertyNameOverride, string fieldName)
    {
        if (!string.IsNullOrEmpty(propertyNameOverride)) return propertyNameOverride;
        return ToCamelCase(fieldName.TrimStart('_'));
    }

    private static PropertyMember CreateReactiveProperty(string propertyName, string fieldName, string type, string comment, AccessModifier accessModifier, bool isVirtual)
        => PropertyMember.Create()
            .WithComment(comment)
            .WithName(propertyName)
            .WithAccessModifier(accessModifier)
            .WithType(type)
            .WithGetter(PropertyAccessorMember.Create()
                .WithAccessModifier(accessModifier)
                .WithCode(fieldName))
            .WithSetter(PropertyMutatorMember.Create()
                .WithAccessModifier(accessModifier)
                .WithCode($"SetProperty(ref {fieldName}, value)"))
            .AsVirtual(isVirtual);

    private static EventMember CreatePropertyChangingEvent(string propertyName, AccessModifier accessModifier, bool isVirtual)
        => EventMember.Create<PropertyChangingEventHandler?>($"{propertyName}Changing", accessModifier)
            .WithComment($"Происходит в момент изменения свойства <see cref=\"{propertyName}\"/>.")
            .WithAdder("PropertyChanging += value")
            .WithRemover("PropertyChanging -= value")
            .AsVirtual(isVirtual);

    private static EventMember CreatePropertyChangedEvent(string propertyName, AccessModifier accessModifier, bool isVirtual)
        => EventMember.Create<PropertyChangedEventHandler?>($"{propertyName}Changed", accessModifier)
            .WithComment($"Происходит после изменения свойства <see cref=\"{propertyName}\"/>.")
            .WithAdder("PropertyChanged += value")
            .WithRemover("PropertyChanged -= value")
            .AsVirtual(isVirtual);

    private static string? BuildSource(string? ns, ClassEntity classBuilder) => SourceBuilder.Create()
        .WithNamespace(ns)
        .WithUsing("System.ComponentModel", "System.Runtime.CompilerServices", "Atom.Architect.Reactive")
        .WithClass(classBuilder)
        .Build(release: true);
}
