using System.Collections.Immutable;
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
public class ReactiveFieldSyntaxProvider : FieldSyntaxProvider
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ReactiveFieldSyntaxProvider"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReactiveFieldSyntaxProvider() => WithAttribute("Reactive");

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void OnExecute(SourceProductionContext context, string entityName, ImmutableArray<SyntaxProviderNodeInfo<IFieldSymbol, FieldDeclarationSyntax>> sources)
    {
        var ns = sources[0].Symbol?.ContainingType.ContainingNamespace.ToDisplayString();
        var classBuilder = ClassEntity.Create(entityName, AccessModifier.Public).AsPartial();

        foreach (var field in sources)
        {
            var node = field.Node;

            foreach (var variable in node.Declaration.Variables)
            {
                var fieldName = variable.Identifier.Text;
                var propertyName = ToCamelCase(fieldName.TrimStart('_'));
                var type = node.Declaration.Type.ToString();

                classBuilder.AddProperty(PropertyMember.Create()
                    .WithName(propertyName)
                    .WithAccessModifier(AccessModifier.Public)
                    .WithType(type)
                    .WithGetter("fieldName")
                    .WithSetter("fieldName => value")
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