using System.Collections.Immutable;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atom.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Atom.Text.Json;

/// <summary>
/// Представляет синтаксический провайдер для <see cref="JsonContextAttribute"/>.
/// </summary>
public class JsonContextTypeSyntaxProvider : TypeSyntaxProvider
{
    private const string AggressiveInliningAttr = "MethodImpl(MethodImplOptions.AggressiveInlining)";
    private const string NotNullAttr = "NotNull";
    private const string DefaultValue = "default";
    private const string CancellationTokenArg = "cancellationToken";
    private const string SerializeMethod = "Serialize";
    private const string SerializeAsyncMethod = "SerializeAsync";
    private const string DeserializeMethod = "Deserialize";
    private const string DeserializeAsyncMethod = "DeserializeAsyncEnumerable";

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="JsonContextTypeSyntaxProvider"/>.
    /// </summary>
    /// <param name="context">Контекст генератора.</param>
    public JsonContextTypeSyntaxProvider(IncrementalGeneratorInitializationContext context) : base(context) => WithAttribute("JsonContext");

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void OnExecute(SourceProductionContext context, string entityName, ImmutableArray<ISyntaxProviderInfo<ITypeSymbol, TypeDeclarationSyntax>> sources)
    {
        var type = sources[0];
        var ns = type.Symbol?.ContainingNamespace.ToDisplayString();

        var converter = type.Node.AttributeLists
            .SelectMany(al => al.Attributes)
            .Where(attr =>
            {
                return attr.Name.ToFullString() is "JsonConverter";
            })
            .Select(x => x.ToFullString()
                .Replace("JsonConverter(typeof(", null)
                .Replace("))", null))
            .LastOrDefault();

        converter = string.IsNullOrEmpty(converter) ? "default" : $"new {converter}()";
        var isNew = true;

        var jsonSourceGenerationAttribute = type.Node.AttributeLists
            .SelectMany(al => al.Attributes)
            .Where(attr =>
            {
                return attr.Name.ToFullString() is "JsonContext";
            })
            .Select(x => x.ToFullString()
                .Replace("JsonContext", null)
                .Replace("JsonKnownNamingPolicy", "JsonNamingPolicy")
                .TrimStart('(')
                .TrimEnd(')'))
            .LastOrDefault();

        var classBuilder = ClassEntity.Create(entityName, AccessModifier.Public)
            .AsPartial()
            .WithParent($"IJsonContext<{entityName}>")
            .WithField(FieldMember.Create<object>("typeInfo", @$"new(() =>
                {{
                    var options = new JsonSerializerOptions
                    {{
                        {jsonSourceGenerationAttribute}
                    }};

                    return new JsonContext<{entityName}>(options, {converter}).TypeInfo;
                }}, true)")
                .WithAccessModifier(AccessModifier.Private)
                .AsReadOnly()
                .AsStatic()
                .WithType($"Lazy<JsonTypeInfo<{entityName}>>"))
            .WithEvent(EventMember.Create("SerializationFailed", AccessModifier.Public)
                .WithType("MutableEventHandler<FailedEventArgs>?")
                .WithComment(InheritdocComment)
                .AsStatic()
                .AsNew(isNew))
            .WithProperty(PropertyMember.CreateWithGetterOnly("TypeInfo", AccessModifier.Public)
                .WithType($"JsonTypeInfo<{entityName}>")
                .AsStatic()
                .WithGetter($"typeInfo.Value")
                .WithComment(InheritdocComment)
                .AsNew(isNew))
            .WithMethod(CreateMethodWithReturn(SerializeMethod, $"JsonSerializer.Serialize(this, TypeInfo)")
                .WithType<string?>()
                .AsNew(isNew))
            .WithMethod(CreateMethod(SerializeMethod, "JsonSerializer.Serialize(utf8json, this, TypeInfo)", MethodArgumentMember.Create("utf8json")
                    .WithType<Stream>(withNullable: default))
                .AsNew(isNew))
            .WithMethod(CreateMethod(SerializeMethod, "JsonSerializer.Serialize(writer, this, TypeInfo)", MethodArgumentMember.Create("writer")
                    .WithType<Utf8JsonWriter>(withNullable: default)
                    .WithAttribute(NotNullAttr))
                .AsNew(isNew))
            .WithMethod(CreateMethodWithReturn("SerializeToDocument", "JsonSerializer.SerializeToDocument(this, TypeInfo)")
                .WithType<JsonDocument?>()
                .AsNew(isNew))
            .WithMethod(CreateMethodWithReturn("SerializeToElement", "JsonSerializer.SerializeToElement(this, TypeInfo)")
                .WithType<JsonElement?>()
                .AsNew(isNew))
            .WithMethod(CreateMethodWithReturn("SerializeToNode", "JsonSerializer.SerializeToNode(this, TypeInfo)")
                .WithType<JsonNode?>()
                .AsNew(isNew))
            .WithMethod(CreateMethodWithReturn("SerializeToUtf8Bytes", "JsonSerializer.SerializeToUtf8Bytes(this, TypeInfo)")
                .WithType<ReadOnlySpan<byte>>()
                .AsNew(isNew))
            .WithMethod(CreateMethod(SerializeAsyncMethod, $"await JsonSerializer.SerializeAsync(utf8json, this, TypeInfo, {CancellationTokenArg}).ConfigureAwait(false)", MethodArgumentMember.Create("utf8json")
                    .WithType<Stream>(withNullable: default)
                    .WithAttribute(NotNullAttr),
                MethodArgumentMember.Create(CancellationTokenArg)
                    .WithType<CancellationToken>()
                    .WithInitialValue(DefaultValue))
                .AsAsync()
                .WithType<ValueTask>()
                .AsNew(isNew))
            .WithMethod(CreateMethod(SerializeAsyncMethod, $"await JsonSerializer.SerializeAsync(writer, this, TypeInfo, {CancellationTokenArg}).ConfigureAwait(false)", MethodArgumentMember.Create("writer")
                    .WithType<PipeWriter>(withNullable: default)
                    .WithAttribute(NotNullAttr),
                MethodArgumentMember.Create(CancellationTokenArg)
                    .WithType<CancellationToken>()
                    .WithInitialValue(DefaultValue))
                .AsAsync()
                .WithType<ValueTask>()
                .AsNew(isNew))
            .WithMethod(CreateMethodWithReturn(DeserializeMethod, "JsonSerializer.Deserialize(json, TypeInfo)", MethodArgumentMember.Create("json")
                    .WithType<JsonDocument>(withNullable: default)
                    .WithAttribute(NotNullAttr))
                .WithType(entityName + '?')
                .AsStatic()
                .AsNew(isNew))
            .WithMethod(CreateMethodWithReturn(DeserializeMethod, "JsonSerializer.Deserialize(json, TypeInfo)", MethodArgumentMember.Create("json")
                    .WithType<JsonElement>())
                .WithType(entityName + '?')
                .AsStatic()
                .AsNew(isNew))
            .WithMethod(CreateMethodWithReturn(DeserializeMethod, "JsonSerializer.Deserialize(utf8json, TypeInfo)", MethodArgumentMember.Create("utf8json")
                    .WithType<Stream>(withNullable: default)
                    .WithAttribute(NotNullAttr))
                .WithType(entityName + '?')
                .AsStatic()
                .AsNew(isNew))
            .WithMethod(CreateMethodWithReturn(DeserializeMethod, "JsonSerializer.Deserialize(json, TypeInfo)", MethodArgumentMember.Create("json")
                    .WithType<string>(withNullable: default)
                    .WithAttribute(NotNullAttr))
                .WithType(entityName + '?')
                .AsStatic()
                .AsNew(isNew))
            .WithMethod(CreateMethodWithReturn(DeserializeMethod, "JsonSerializer.Deserialize(node, TypeInfo)", MethodArgumentMember.Create("node")
                    .WithType<JsonNode?>())
                .WithType(entityName + '?')
                .AsStatic()
                .AsNew(isNew))
            .WithMethod(CreateMethodWithReturn(DeserializeMethod, "JsonSerializer.Deserialize(ref reader, TypeInfo)", MethodArgumentMember.Create("reader")
                    .WithType<Utf8JsonReader>()
                    .AsRef())
                .WithType(entityName + '?')
                .AsStatic()
                .AsNew(isNew))
            .WithMethod(CreateMethodWithReturn(DeserializeMethod, "JsonSerializer.Deserialize(json, TypeInfo)", MethodArgumentMember.Create("json")
                    .WithType<ReadOnlySpan<byte>>())
                .WithType(entityName + '?')
                .AsStatic()
                .AsNew(isNew))
            .WithMethod(CreateMethodWithReturn(DeserializeMethod, "JsonSerializer.Deserialize(json, TypeInfo)", MethodArgumentMember.Create("json")
                    .WithType<ReadOnlySpan<char>>())
                .WithType(entityName + '?')
                .AsStatic()
                .AsNew(isNew))
            .WithMethod(CreateMethodWithReturn("DeserializeAsync", $"await JsonSerializer.DeserializeAsync(utf8json, TypeInfo, {CancellationTokenArg}).ConfigureAwait(false)", MethodArgumentMember.Create("utf8json")
                    .WithType<Stream>(withNullable: default)
                    .WithAttribute(NotNullAttr),
                MethodArgumentMember.Create(CancellationTokenArg)
                    .WithType<CancellationToken>()
                    .WithInitialValue(DefaultValue))
                .WithType($"ValueTask<{entityName}?>")
                .AsStatic()
                .AsAsync()
                .AsNew(isNew))
            .WithMethod(MethodMember.Create(DeserializeAsyncMethod, AccessModifier.Public)
                .WithComment(InheritdocComment)
                .WithAttribute(AggressiveInliningAttr)
                .WithArgument(MethodArgumentMember.Create("utf8json")
                    .WithType<Stream>(withNullable: default).WithAttribute(NotNullAttr),
                MethodArgumentMember.Create("topLevelValues")
                    .WithType<bool>(),
                MethodArgumentMember.Create(CancellationTokenArg)
                    .WithType<CancellationToken>()
                    .WithInitialValue(DefaultValue))
                .WithCode($"JsonSerializer.DeserializeAsyncEnumerable(utf8json, TypeInfo, topLevelValues, {CancellationTokenArg})")
                .WithType($"IAsyncEnumerable<{entityName}?>")
                .AsStatic()
                .AsNew(isNew))
            .WithMethod(MethodMember.Create(DeserializeAsyncMethod, AccessModifier.Public)
                .WithComment(InheritdocComment)
                .WithAttribute(AggressiveInliningAttr)
                .WithArgument(MethodArgumentMember.Create("utf8json")
                    .WithType<Stream>(withNullable: default).WithAttribute(NotNullAttr),
                MethodArgumentMember.Create(CancellationTokenArg)
                    .WithType<CancellationToken>()
                    .WithInitialValue(DefaultValue))
                .WithCode($"JsonSerializer.DeserializeAsyncEnumerable(utf8json, TypeInfo, {CancellationTokenArg})")
                .WithType($"IAsyncEnumerable<{entityName}?>")
                .AsStatic()
                .AsNew(isNew));

        var sourceCode = SourceBuilder.Create()
            .WithNamespace(ns)
            .WithDirective("#pragma warning disable CS0109")
            .WithUsing("System.IO",
                "System.IO.Pipelines",
                "System.Diagnostics.CodeAnalysis",
                "System.Runtime.CompilerServices",
                "System.Text.Json",
                "System.Text.Json.Nodes",
                "System.Text.Json.Serialization",
                "System.Text.Json.Serialization.Metadata",
                "Atom.Text.Json")
            .WithClass(classBuilder);

        var src = sourceCode.Build(true);
        if (!string.IsNullOrEmpty(src)) context.AddSource($"{entityName}.Json.g.cs", SourceText.From(src, Encoding.UTF8));
    }

    private static MethodMember CreateMethodWithReturn(string name, string body, params MethodArgumentMember[] args)
    {
        return MethodMember.Create(name, AccessModifier.Public)
            .WithComment(InheritdocComment)
            .WithArgument(args)
            .WithAttribute(AggressiveInliningAttr)
            .WithCode(@$"
                try
                {{
                    return {body};
                }}
                catch (JsonException ex)
                {{
                    SerializationFailed.On(args => args.Exception = ex);
                    return default;
                }}");
    }

    private static MethodMember CreateMethod(string name, string body, params MethodArgumentMember[] args)
    {
        return MethodMember.Create(name, AccessModifier.Public)
            .WithComment(InheritdocComment)
            .WithArgument(args)
            .WithAttribute(AggressiveInliningAttr)
            .WithCode(@$"
                try
                {{
                    {body};
                }}
                catch (JsonException ex)
                {{
                    SerializationFailed.On(args => args.Exception = ex);
                }}");
    }
}