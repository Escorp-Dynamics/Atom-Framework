using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;

namespace System.Text.Json.Serialization;

/// <summary>
/// Представляет расширяемый конвертер сериализации JSON.
/// </summary>
/// <typeparam name="T">Тип объекта.</typeparam>
public abstract class ExtendableJsonConverter<T> : JsonConverter<T> where T : new()
{
    /// <summary>
    /// Читает значение из указанного читателя JSON и десериализует его в экземпляр.
    /// </summary>
    /// <param name="reader">Читатель JSON, из которого будет считываться значение.</param>
    /// <param name="root">Корневой элемент.</param>
    /// <param name="typeToConvert">Тип, в который будет десериализовано значение.</param>
    /// <param name="options">Опции сериализации JSON.</param>
    /// <returns>Десериализованный экземпляр, если операция чтения прошла успешно; иначе - <c>null</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected abstract T? OnReading(ref Utf8JsonReader reader, JsonElement root, Type typeToConvert, JsonSerializerOptions options);

    /// <summary>
    /// Записывает указанный экземпляр в указанного писателя JSON.
    /// </summary>
    /// <param name="writer">Писатель JSON, в который будет записываться значение.</param>
    /// <param name="value">Значение, которое будет сериализовано.</param>
    /// <param name="options">Опции сериализации JSON.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected abstract void OnWriting(Utf8JsonWriter writer, T value, JsonSerializerOptions options);

    /// <summary>
    /// Записывает свойство в указанного писателя JSON.
    /// </summary>
    /// <param name="writer">Писатель JSON, в который будет записываться значение.</param>
    /// <param name="propertyName">Имя свойства.</param>
    /// <param name="value">Значение свойства.</param>
    /// <param name="options">Опции сериализации JSON.</param>
    /// <param name="typeInfo">Информация о типе.</param>
    /// <param name="converter">Конвертер типа.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void WriteProperty<TValue>([NotNull] Utf8JsonWriter writer, string propertyName, TValue? value, [NotNull] JsonSerializerOptions options, JsonTypeInfo<TValue>? typeInfo = default, JsonConverter<TValue>? converter = default)
    {
        if (ShouldIgnoreValue(options, value)) return;

        propertyName = ExtendableJsonConverter<T>.ApplyNamingPolicy(options, propertyName);
        writer.WritePropertyName(propertyName);

        if (value is not null)
        {
            if (typeInfo is not null)
            {
                JsonSerializer.Serialize(writer, value, typeInfo);
                return;
            }

            if (converter is not null && converter.CanConvert(typeof(TValue)))
            {
                converter.Write(writer, value, options);
                return;
            }
        }

        WriteValue(writer, value);
    }

    /// <summary>
    /// Читает значение из указанного читателя JSON и десериализует его в экземпляр.
    /// </summary>
    /// <param name="reader">Читатель JSON, из которого будет считываться значение.</param>
    /// <param name="typeToConvert">Тип, в который будет десериализовано значение.</param>
    /// <param name="options">Опции сериализации JSON.</param>
    /// <returns>Десериализованный экземпляр, если операция чтения прошла успешно; иначе - <c>null</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (!JsonDocument.TryParseValue(ref reader, out var document)) return default;
        using (document) return OnReading(ref reader, document.RootElement, typeToConvert, options);
    }

    /// <summary>
    /// Записывает указанный экземпляр в указанного писателя JSON.
    /// </summary>
    /// <param name="writer">Писатель JSON, в который будет записываться значение.</param>
    /// <param name="value">Значение, которое будет сериализовано.</param>
    /// <param name="options">Опции сериализации JSON.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write([NotNull] Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        OnWriting(writer, value, options);
        writer.WriteEndObject();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteValue<TValue>(Utf8JsonWriter writer, TValue? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            writer.Flush();
            return;
        }

        switch (value)
        {
            case string sv:
                writer.WriteStringValue(sv);
                break;
            case bool bv:
                writer.WriteBooleanValue(bv);
                break;
            case byte:
            case sbyte:
            case ushort:
            case short:
            case uint:
            case int:
            case ulong:
            case long:
            case float:
            case double:
            case decimal:
                writer.WriteNumberValue(Convert.ToDecimal(value));
                break;
            case Enum enumValue:
                writer.WriteNumberValue(Convert.ToInt32(enumValue));
                break;
            case DateTime dt:
                writer.WriteStringValue(dt);
                break;
            case DateTimeOffset dto:
                writer.WriteStringValue(dto);
                break;
            case Guid g:
                writer.WriteStringValue(g);
                break;
            default:
                throw new AggregateException();
        }

        writer.Flush();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ApplyNamingPolicy(JsonSerializerOptions options, string propertyName) => options.PropertyNamingPolicy?.ConvertName(propertyName) ?? propertyName;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldIgnoreValue<TValue>(JsonSerializerOptions options, TValue? value)
    {
        if (options is null || options.DefaultIgnoreCondition is JsonIgnoreCondition.Never) return default;

        return options.DefaultIgnoreCondition switch
        {
            JsonIgnoreCondition.WhenWritingNull => value is null,
            JsonIgnoreCondition.WhenWritingDefault => IsDefaultValue(value),
            JsonIgnoreCondition.Always => true,
            _ => default,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDefaultValue<TValue>(TValue? value)
    {
        if (value is null) return true;
        if (value is string str) return string.IsNullOrEmpty(str);
        if (value is IEnumerable<char> charEnumerable) return string.IsNullOrEmpty(new string([.. charEnumerable]));
        if (value is IEnumerable enumerable) return !enumerable.GetEnumerator().MoveNext();

        return EqualityComparer<TValue>.Default.Equals(value, default);
    }
}