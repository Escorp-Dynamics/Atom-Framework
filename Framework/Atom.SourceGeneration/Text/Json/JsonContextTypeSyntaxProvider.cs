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
    public JsonContextTypeSyntaxProvider(IncrementalGeneratorInitializationContext context) : base(context) => WithAttribute();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WithAttribute() => WithAttribute("JsonContext");

    /// <inheritdoc/>
    protected override void OnExecute(SourceProductionContext context, string entityName, ImmutableArray<ISyntaxProviderInfo<ITypeSymbol, TypeDeclarationSyntax>> sources)
    {
        var type = sources[0];
        var ns = type.Symbol?.ContainingNamespace.ToDisplayString();
        var converterLiteral = GetConverterLiteral(type.Node);
        var jsonOptions = GetJsonContextOptions(type.Node);

        var classBuilder = CreateJsonContextClass(entityName, converterLiteral, jsonOptions);
        var source = BuildSource(ns, classBuilder);
        if (!string.IsNullOrEmpty(source))
            context.AddSource($"{entityName}.Json.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static string GetConverterLiteral(TypeDeclarationSyntax node)
    {
        var converter = node.AttributeLists
            .SelectMany(static al => al.Attributes)
            .Where(static attr => attr.Name.ToFullString() is "JsonConverter")
            .Select(static x => x.ToFullString()
                .Replace("JsonConverter(typeof(", newValue: null, StringComparison.Ordinal)
                .Replace("))", newValue: null, StringComparison.Ordinal))
            .LastOrDefault();

        return string.IsNullOrEmpty(converter) ? "default" : $"new {converter}()";
    }

    private static string? GetJsonContextOptions(TypeDeclarationSyntax node)
        => node.AttributeLists
            .SelectMany(static al => al.Attributes)
            .Where(static attr => attr.Name.ToFullString() is "JsonContext")
            .Select(static x => x.ToFullString()
                .Replace("JsonContext", newValue: null, StringComparison.Ordinal)
                .Replace("JsonKnownNamingPolicy", "JsonNamingPolicy", StringComparison.Ordinal)
                .TrimStart('(')
                .TrimEnd(')'))
            .LastOrDefault();

    private static ClassEntity CreateJsonContextClass(string entityName, string converterLiteral, string? jsonOptions)
    {
        var builder = CreateClassSkeleton(entityName, converterLiteral, jsonOptions);
        builder = builder.WithMethod(GetSerializeMethods());
        builder = builder.WithMethod(GetDeserializeSyncMethods(entityName));
        builder = builder.WithMethod(GetDeserializeAsyncMethods(entityName));
        return builder;
    }

    private static ClassEntity CreateClassSkeleton(string entityName, string converterLiteral, string? jsonOptions)
    {
        const bool isNew = true;
        return ClassEntity.Create(entityName, AccessModifier.Public)
            .AsPartial()
            .WithParent($"IJsonContext<{entityName}>")
            .WithField(CreateTypeInfoField(entityName, converterLiteral, jsonOptions))
            .WithEvent(EventMember.Create("SerializationFailed", AccessModifier.Public)
                .WithType("MutableEventHandler<object, FailedEventArgs>?")
                .WithComment(InheritdocComment)
                .AsStatic()
                .AsNew(isNew))
            .WithProperty(PropertyMember.CreateWithGetterOnly("TypeInfo", AccessModifier.Public)
                .WithType($"JsonTypeInfo<{entityName}>")
                .AsStatic()
                .WithGetter("typeInfo.Value")
                .WithComment(InheritdocComment)
                .AsNew(isNew));
    }

    private static FieldMember CreateTypeInfoField(string entityName, string converterLiteral, string? jsonOptions)
    {
        var options = jsonOptions ?? string.Empty;
        var initializer = @$"new(() =>
                {{
                    var options = new JsonSerializerOptions
                    {{
                        {options}
                    }};

                    return new JsonContext<{entityName}>(options, {converterLiteral}).TypeInfo;
                }}, true)";

        return FieldMember.Create<object>("typeInfo", initializer)
            .WithAccessModifier(AccessModifier.Private)
            .AsReadOnly()
            .AsStatic()
            .WithType($"Lazy<JsonTypeInfo<{entityName}>>");
    }

    private static IEnumerable<MethodMember> GetSerializeMethods()
    {
        const bool isNew = true;

        yield return CreateMethodWithReturn(SerializeMethod, "JsonSerializer.Serialize(this, TypeInfo)")
            .WithType<string?>()
            .AsNew(isNew);

        yield return CreateMethod(SerializeMethod, "JsonSerializer.Serialize(utf8json, this, TypeInfo)", MethodArgumentMember.Create("utf8json")
                .WithType<Stream>(withNullable: default))
            .AsNew(isNew);

        yield return CreateMethod(SerializeMethod, "JsonSerializer.Serialize(writer, this, TypeInfo)", MethodArgumentMember.Create("writer")
                .WithType<Utf8JsonWriter>(withNullable: default)
                .WithAttribute(NotNullAttr))
            .AsNew(isNew);

        yield return CreateMethodWithReturn("SerializeToDocument", "JsonSerializer.SerializeToDocument(this, TypeInfo)")
            .WithType<JsonDocument?>()
            .AsNew(isNew);

        yield return CreateMethodWithReturn("SerializeToElement", "JsonSerializer.SerializeToElement(this, TypeInfo)")
            .WithType<JsonElement?>()
            .AsNew(isNew);

        yield return CreateMethodWithReturn("SerializeToNode", "JsonSerializer.SerializeToNode(this, TypeInfo)")
            .WithType<JsonNode?>()
            .AsNew(isNew);

        yield return CreateMethodWithReturn("SerializeToUtf8Bytes", "JsonSerializer.SerializeToUtf8Bytes(this, TypeInfo)")
            .WithType<ReadOnlySpan<byte>>()
            .AsNew(isNew);

        yield return CreateMethod(SerializeAsyncMethod, $"await JsonSerializer.SerializeAsync(utf8json, this, TypeInfo, {CancellationTokenArg}).ConfigureAwait(false)", MethodArgumentMember.Create("utf8json")
                .WithType<Stream>(withNullable: default)
                .WithAttribute(NotNullAttr),
            MethodArgumentMember.Create(CancellationTokenArg)
                .WithType<CancellationToken>()
                .WithInitialValue(DefaultValue))
            .AsAsync()
            .WithType<ValueTask>()
            .AsNew(isNew);

        yield return CreateMethod(SerializeAsyncMethod, $"await JsonSerializer.SerializeAsync(writer, this, TypeInfo, {CancellationTokenArg}).ConfigureAwait(false)", MethodArgumentMember.Create("writer")
                .WithType<PipeWriter>(withNullable: default)
                .WithAttribute(NotNullAttr),
            MethodArgumentMember.Create(CancellationTokenArg)
                .WithType<CancellationToken>()
                .WithInitialValue(DefaultValue))
            .AsAsync()
            .WithType<ValueTask>()
            .AsNew(isNew);
    }

    private static IEnumerable<MethodMember> GetDeserializeSyncMethods(string entityName)
    {
        const bool isNew = true;

        yield return CreateStaticMethodWithReturn(DeserializeMethod, "JsonSerializer.Deserialize(json, TypeInfo)", MethodArgumentMember.Create("json")
                .WithType<JsonDocument>(withNullable: default)
                .WithAttribute(NotNullAttr))
            .WithType(entityName + '?')
            .AsStatic()
            .AsNew(isNew);

        yield return CreateStaticMethodWithReturn(DeserializeMethod, "JsonSerializer.Deserialize(json, TypeInfo)", MethodArgumentMember.Create("json")
                .WithType<JsonElement>())
            .WithType(entityName + '?')
            .AsStatic()
            .AsNew(isNew);

        yield return CreateStaticMethodWithReturn(DeserializeMethod, "JsonSerializer.Deserialize(utf8json, TypeInfo)", MethodArgumentMember.Create("utf8json")
                .WithType<Stream>(withNullable: default)
                .WithAttribute(NotNullAttr))
            .WithType(entityName + '?')
            .AsStatic()
            .AsNew(isNew);

        yield return CreateStaticMethodWithReturn(DeserializeMethod, "JsonSerializer.Deserialize(json, TypeInfo)", MethodArgumentMember.Create("json")
                .WithType<string>(withNullable: default)
                .WithAttribute(NotNullAttr))
            .WithType(entityName + '?')
            .AsStatic()
            .AsNew(isNew);

        yield return CreateStaticMethodWithReturn(DeserializeMethod, "JsonSerializer.Deserialize(node, TypeInfo)", MethodArgumentMember.Create("node")
                .WithType<JsonNode?>())
            .WithType(entityName + '?')
            .AsStatic()
            .AsNew(isNew);

        yield return CreateStaticMethodWithReturn(DeserializeMethod, "JsonSerializer.Deserialize(ref reader, TypeInfo)", MethodArgumentMember.Create("reader")
                .WithType<Utf8JsonReader>()
                .AsRef())
            .WithType(entityName + '?')
            .AsStatic()
            .AsNew(isNew);

        yield return CreateStaticMethodWithReturn(DeserializeMethod, "JsonSerializer.Deserialize(json, TypeInfo)", MethodArgumentMember.Create("json")
                .WithType<ReadOnlySpan<byte>>())
            .WithType(entityName + '?')
            .AsStatic()
            .AsNew(isNew);

        yield return CreateStaticMethodWithReturn(DeserializeMethod, "JsonSerializer.Deserialize(json, TypeInfo)", MethodArgumentMember.Create("json")
                .WithType<ReadOnlySpan<char>>())
            .WithType(entityName + '?')
            .AsStatic()
            .AsNew(isNew);
    }

    private static IEnumerable<MethodMember> GetDeserializeAsyncMethods(string entityName)
    {
        const bool isNew = true;

        yield return CreateStaticMethodWithReturn("DeserializeAsync", $"await JsonSerializer.DeserializeAsync(utf8json, TypeInfo, {CancellationTokenArg}).ConfigureAwait(false)", MethodArgumentMember.Create("utf8json")
                .WithType<Stream>(withNullable: default)
                .WithAttribute(NotNullAttr),
            MethodArgumentMember.Create(CancellationTokenArg)
                .WithType<CancellationToken>()
                .WithInitialValue(DefaultValue))
            .WithType($"ValueTask<{entityName}?>")
            .AsStatic()
            .AsAsync()
            .AsNew(isNew);

        yield return MethodMember.Create(DeserializeAsyncMethod, AccessModifier.Public)
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
            .AsNew(isNew);

        yield return MethodMember.Create(DeserializeAsyncMethod, AccessModifier.Public)
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
            .AsNew(isNew);
    }

    private static string? BuildSource(string? ns, ClassEntity classBuilder)
        => SourceBuilder.Create()
            .WithNamespace(ns)
            .WithDirective("#pragma warning disable CS0109")
            .WithUsing(
                "System.IO",
                "System.IO.Pipelines",
                "System.Diagnostics.CodeAnalysis",
                "System.Runtime.CompilerServices",
                "System.Text.Json",
                "System.Text.Json.Nodes",
                "System.Text.Json.Serialization",
                "System.Text.Json.Serialization.Metadata",
                "Atom.Text.Json")
            .WithClass(classBuilder)
            .Build(release: true);

    private static MethodMember CreateMethodWithReturn(string name, string body, params IEnumerable<MethodArgumentMember> args)
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
                    SerializationFailed.On(this, args => args.Exception = ex);
                    return default;
                }}");
    }

    private static MethodMember CreateStaticMethodWithReturn(string name, string body, params IEnumerable<MethodArgumentMember> args)
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
                    SerializationFailed.On(string.Empty, args => args.Exception = ex);
                    return default;
                }}");
    }

    private static MethodMember CreateMethod(string name, string body, params IEnumerable<MethodArgumentMember> args)
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
                    SerializationFailed.On(this, args => args.Exception = ex);
                }}");
    }
}
