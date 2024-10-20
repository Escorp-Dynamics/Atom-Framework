using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Atom.SourceGeneration;

/// <summary>
/// Представляет расширения генератора.
/// </summary>
public static class Extensions
{
    private static T? TryConvertTo<T>(object? value) => value is T v ? v : default;

    /// <summary>
    /// Добавляет синтаксический провайдер к контексту инкрементального генератора.
    /// </summary>
    /// <param name="context">Контекст инкрементального генератора.</param>
    /// <param name="generatorProvider">Синтаксический провайдер генератора.</param>
    /// <param name="analyzerProvider">Синтаксический провайдер анализатора.</param>
    /// <typeparam name="TSymbol">Тип символа.</typeparam>
    /// <typeparam name="TSyntaxNode">Тип синтаксического узла провайдера.</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IncrementalGeneratorInitializationContext UseProvider<TSymbol, TSyntaxNode>(this IncrementalGeneratorInitializationContext context,
        [NotNull] ISyntaxProvider<TSymbol, TSyntaxNode> generatorProvider, IAnalyzerSyntaxProvider? analyzerProvider)
        where TSymbol : ISymbol
        where TSyntaxNode : SyntaxNode
    {
        var syntaxProvider = context.SyntaxProvider
            .CreateSyntaxProvider(generatorProvider.Predicate, generatorProvider.Transform)
            .Where(static m => m != default);

        context.RegisterSourceOutput(syntaxProvider.Collect(), generatorProvider.Execute);

        if (analyzerProvider is null) return context;

        var diagnostics = context.CompilationProvider.SelectMany((compilation, _) =>
            compilation.GetDiagnostics(_)
            .Where(d => d.Id == analyzerProvider.Id));

        context.RegisterSourceOutput(diagnostics, (context, diagnostic) =>
        {
            if (diagnostic.Location.SourceTree?.GetRoot().FindNode(diagnostic.Location.SourceSpan) is not TSyntaxNode node) return;
            generatorProvider.Execute(context, [new SyntaxProviderInfo<TSymbol, TSyntaxNode>(node)]);
        });

        return context;
    }

    /// <summary>
    /// Добавляет синтаксический провайдер к контексту инкрементального генератора.
    /// </summary>
    /// <param name="context">Контекст инкрементального генератора.</param>
    /// <param name="generatorProvider">Синтаксический провайдер генератора.</param>
    /// <typeparam name="TSymbol">Тип символа.</typeparam>
    /// <typeparam name="TSyntaxNode">Тип синтаксического узла провайдера.</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IncrementalGeneratorInitializationContext UseProvider<TSymbol, TSyntaxNode>(this IncrementalGeneratorInitializationContext context,
        [NotNull] ISyntaxProvider<TSymbol, TSyntaxNode> generatorProvider)
        where TSymbol : ISymbol
        where TSyntaxNode : SyntaxNode
        => context.UseProvider(generatorProvider, default);

    /// <summary>
    /// Добавляет провайдер синтаксического анализа к контексту анализатора.
    /// </summary>
    /// <param name="context">Контекст анализатора.</param>
    /// <param name="provider">Провайдер.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static AnalysisContext UseProvider([NotNull] this AnalysisContext context, [NotNull] IAnalyzerSyntaxProvider provider)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(provider.Execute, provider.SyntaxKinds);
        return context;
    }

    /// <summary>
    /// Парсит документированный комментарий.
    /// </summary>
    /// <param name="symbol">Синтаксический символ.</param>
    /// <param name="summary">Общий комментарий.</param>
    /// <param name="paramComments">Комментарий параметра.</param>
    /// <param name="typeparamComments">Комментарий параметра типа.</param>
    /// <param name="returnsComment">Комментарий возврата.</param>
    /// <param name="setterComment">комментарий мутатора.</param>
    /// <param name="remarks">Ремарки.</param>
    public static ISymbol TryParseXmlDocumentation(
        [NotNull] this ISymbol symbol,
        out string? summary,
        out IDictionary<string, string>? paramComments,
        out IDictionary<string, string>? typeparamComments,
        out string? returnsComment,
        out string? setterComment,
        out string? remarks)
    {
        summary = default;
        paramComments = default;
        typeparamComments = default;
        returnsComment = default;
        setterComment = default;
        remarks = default;

        var xmlDocumentation = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrEmpty(xmlDocumentation)) return symbol;

        try
        {
            var xmlDoc = XDocument.Parse(xmlDocumentation);
            if (xmlDoc is null || xmlDoc.Root is null) return symbol;

            foreach (var element in xmlDoc.Root.Elements())
            {
                var elementName = element.Name.LocalName;
                if (string.IsNullOrEmpty(elementName)) continue;

                switch (elementName)
                {
                    case "summary":
                        summary = element.Value.Trim();
                        break;

                    case "returns":
                        returnsComment = element.Value.Trim();
                        break;

                    case "value":
                        setterComment = element.Value.Trim();
                        break;

                    case "remarks":
                        remarks = element.Value.Trim();
                        break;

                    case "param":
                        var paramName = element.Attribute("name")?.Value;

                        if (!string.IsNullOrEmpty(paramName))
                        {
                            paramComments ??= new Dictionary<string, string>();
                            paramComments[paramName] = element.Value.Trim();
                        }
                        break;

                    case "typeparam":
                        var typeparamName = element.Attribute("name")?.Value;

                        if (!string.IsNullOrEmpty(typeparamName))
                        {
                            typeparamComments ??= new Dictionary<string, string>();
                            typeparamComments[typeparamName] = element.Value.Trim();
                        }
                        break;
                }
            }
        }
        catch
        {
            // Noncompliant: is the block empty on purpose, or is code missing?
        }

        return symbol;
    }

    /// <summary>
    /// Парсит документированный комментарий.
    /// </summary>
    /// <param name="symbol">Синтаксический символ.</param>
    /// <param name="summary">Общий комментарий.</param>
    public static ISymbol TryParseXmlDocumentation(this ISymbol symbol, out string? summary)
        => symbol.TryParseXmlDocumentation(out summary, out _, out _, out _, out _, out _);

    /// <summary>
    /// Возвращает параметр атрибута.
    /// </summary>
    /// <param name="attribute">Данные атрибута.</param>
    /// <param name="name">Имя параметра.</param>
    /// <typeparam name="T">Тип данных.</typeparam>
    public static T? GetParameter<T>([NotNull] this AttributeData attribute, [NotNull] string name)
    {
        var typeName = typeof(T).GetFriendlyName(default).TrimEnd('?');

        var constructorArg = attribute.ConstructorArguments
            .FirstOrDefault(arg => arg.Type is not null && typeName.Equals(arg.Type.Name, StringComparison.InvariantCultureIgnoreCase));

        if (!constructorArg.IsNull) return TryConvertTo<T>(constructorArg.Value);

        var namedArg = attribute.NamedArguments
            .FirstOrDefault(arg => name.Equals(arg.Key, StringComparison.InvariantCultureIgnoreCase)).Value;

        if (!namedArg.IsNull) return TryConvertTo<T>(namedArg.Value);

        return default;
    }
}