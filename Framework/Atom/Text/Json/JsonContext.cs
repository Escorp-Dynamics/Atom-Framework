#pragma warning disable CA1721

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Atom.Text.Json;

/// <summary>
/// Представляет контекст сериализации JSON.
/// </summary>
/// <typeparam name="T">Тип объекта сериализации.</typeparam>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="JsonContext{T}"/>.
/// </remarks>
/// <param name="options">Опции сериализации.</param>
/// <param name="converter">Конвертер типа.</param>
public class JsonContext<T>(JsonSerializerOptions options, JsonConverter? converter) : JsonSerializerContext(options), IJsonTypeInfoResolver
{
    private readonly JsonSerializerOptions options = options;
    private readonly JsonConverter? converter = converter;

    /// <summary>
    /// Сгенерированные опции сериализации.
    /// </summary>
    protected override JsonSerializerOptions? GeneratedSerializerOptions => options;

    /// <summary>
    /// Метаданные сериализации.
    /// </summary>
    public virtual JsonTypeInfo<T> TypeInfo => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="JsonContext{T}"/>.
    /// </summary>
    /// <param name="options">Опции сериализации.</param>
    public JsonContext(JsonSerializerOptions options) : this(options, default) { }

    /// <summary>
    /// Возвращает метаданные типа.
    /// </summary>
    /// <param name="type">Тип.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override JsonTypeInfo? GetTypeInfo(Type type)
    {
        Options.TryGetTypeInfo(type, out var typeInfo);
        return typeInfo;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    JsonTypeInfo? IJsonTypeInfoResolver.GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        if (type == typeof(T))
        {
            if (!TryGetTypeInfoForRuntimeCustomConverter(options, out var jsonTypeInfo))
            {
                var c = converter is not null
                    ? ExpandConverter(typeof(T), converter, options)
                    : GetRuntimeConverterForType(typeof(T), options);

                if (c is not null) jsonTypeInfo = JsonMetadataServices.CreateValueInfo<T>(options, c);
            }

            if (jsonTypeInfo is null) return default;

            jsonTypeInfo.OriginatingResolver = this;
            return jsonTypeInfo;
        }

        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsonConverter ExpandConverter(Type type, JsonConverter converter, JsonSerializerOptions options, bool validateCanConvert = true)
    {
        if (validateCanConvert && !converter.CanConvert(type))
            throw new InvalidOperationException($"Конвертер '{converter.GetType()}' не совместим с типом '{type}'");

        if (converter is JsonConverterFactory factory)
        {
            var c = factory.CreateConverter(type, options);

            if (c is null or JsonConverterFactory)
            {
                throw new InvalidOperationException($"конвертер '{factory.GetType()}' не может возвращать null или экземпляр типа JsonConverterFactory");
            }

            converter = c;
        }

        return converter;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static JsonConverter? GetRuntimeConverterForType(Type type, JsonSerializerOptions options)
    {
        for (var i = 0; i < options.Converters.Count; ++i)
        {
            var converter = options.Converters[i];
            if (converter?.CanConvert(type) is true) return ExpandConverter(type, converter, options, validateCanConvert: false);
        }

        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetTypeInfoForRuntimeCustomConverter(JsonSerializerOptions options, out JsonTypeInfo<T>? jsonTypeInfo)
    {
        var converter = GetRuntimeConverterForType(typeof(T), options);

        if (converter is not null)
        {
            jsonTypeInfo = JsonMetadataServices.CreateValueInfo<T>(options, converter);
            return true;
        }

        jsonTypeInfo = default;
        return default;
    }
}