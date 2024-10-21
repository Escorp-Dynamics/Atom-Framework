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
/// Представляет синтаксический провайдер для реактивных полей.
/// </summary>
public class ReactivelyFieldSyntaxProvider : FieldSyntaxProvider
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ReactivelyFieldSyntaxProvider"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReactivelyFieldSyntaxProvider() => WithAttribute("Reactively");

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void OnExecute(SourceProductionContext context, string entityName, ImmutableArray<ISyntaxProviderInfo<IFieldSymbol, FieldDeclarationSyntax>> sources)
    {
        var ns = sources[0].Symbol?.ContainingType.ContainingNamespace.ToDisplayString();
        var classBuilder = ClassEntity.Create(entityName, AccessModifier.Public).AsPartial();

        foreach (var field in sources)
        {
            var node = field.Node;

            var type = field.Symbol?.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? node.Declaration.Type.ToString();
            if (!type.EndsWith('?') && field.Symbol?.Type.NullableAnnotation is NullableAnnotation.Annotated) type += "?";
            
            var comment = string.Empty;
            field.Symbol?.TryParseXmlDocumentation(out comment);

            var attribute = field.Symbol?.GetAttributes()
                .FirstOrDefault(attr => attr.AttributeClass?.Name is nameof(ReactivelyAttribute));

            var propertyName = attribute!.GetParameter<string>(nameof(ReactivelyAttribute.PropertyName));
            var accessModifier = attribute.GetParameter<AccessModifier?>(nameof(ReactivelyAttribute.AccessModifier)) ?? AccessModifier.Public;
            var asVirtual = attribute.GetParameter<bool?>(nameof(ReactivelyAttribute.IsVirtual));

            foreach (var variable in node.Declaration.Variables)
            {
                var fieldName = variable.Identifier.Text;
                if (string.IsNullOrEmpty(propertyName)) propertyName = ToCamelCase(fieldName.TrimStart('_'));

                classBuilder.AddProperty(PropertyMember.Create()
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
                    .AsVirtual(asVirtual ?? default)
                );

                classBuilder.AddEvent(EventMember.Create<PropertyChangingEventHandler>($"{propertyName}Changing", accessModifier)
                    .WithComment($"Происходит в момент изменения свойства <see cref=\"{propertyName}\"/>.")
                    .WithAdder("PropertyChanging += value")
                    .WithRemover("PropertyChanging -= value")
                    .AsVirtual(asVirtual ?? default)
                );

                classBuilder.AddEvent(EventMember.Create<PropertyChangedEventHandler>($"{propertyName}Changed", accessModifier)
                    .WithComment($"Происходит после изменения свойства <see cref=\"{propertyName}\"/>.")
                    .WithAdder("PropertyChanged += value")
                    .WithRemover("PropertyChanged -= value")
                    .AsVirtual(asVirtual ?? default)
                );
            }
        }

        var sourceCode = SourceBuilder.Create()
            .WithNamespace(ns)
            .AddClass(classBuilder);

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