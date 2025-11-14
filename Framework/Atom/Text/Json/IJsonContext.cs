#pragma warning disable CA1000

using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Atom.Text.Json;

/// <summary>
/// Представляет базовый интерфейс для реализации сериализуемых моделей JSON.
/// </summary>
/// <typeparam name="T">Тип модели.</typeparam>
public interface IJsonContext<T>
{
    /// <summary>
    /// Происходит в момент неудачной сериализации.
    /// </summary>
    static abstract event MutableEventHandler<object, FailedEventArgs>? SerializationFailed;

    /// <summary>
    /// Метаданные типа.
    /// </summary>
    static abstract JsonTypeInfo<T> TypeInfo { get; }

    /// <summary>
    /// Сериализует текущий объект в строку.
    /// </summary>
    string? Serialize();

    /// <summary>
    /// Сериализует текущий объект в поток.
    /// </summary>
    /// <param name="utf8json">Поток сериализации.</param>
    void Serialize(Stream utf8json);

    /// <summary>
    /// Сериализует текущий объект в райтер.
    /// </summary>
    /// <param name="writer">Райтер сериализации.</param>
    void Serialize(Utf8JsonWriter writer);

    /// <summary>
    /// Сериализует текущий объект в <see cref="JsonDocument"/>.
    /// </summary>
    JsonDocument? SerializeToDocument();

    /// <summary>
    /// Сериализует текущий объект в <see cref="JsonElement"/>.
    /// </summary>
    JsonElement? SerializeToElement();

    /// <summary>
    /// Сериализует текущий объект в <see cref="JsonNode"/>.
    /// </summary>
    JsonNode? SerializeToNode();

    /// <summary>
    /// Сериализует текущий объект в массив байт.
    /// </summary>
    ReadOnlySpan<byte> SerializeToUtf8Bytes();

    /// <summary>
    /// Сериализует текущий объект в поток сериализации.
    /// </summary>
    /// <param name="utf8json">Поток сериализации.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask SerializeAsync(Stream utf8json, CancellationToken cancellationToken = default);

    /// <summary>
    /// Сериализует текущий объект в поток сериализации.
    /// </summary>
    /// <param name="writer">Поток сериализации.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask SerializeAsync(PipeWriter writer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Десериализует <see cref="JsonDocument"/>.
    /// </summary>
    /// <param name="json">Документ json.</param>
    static abstract T? Deserialize(JsonDocument json);

    /// <summary>
    /// Десериализует <see cref="JsonElement"/>.
    /// </summary>
    /// <param name="json">Элемент json.</param>
    static abstract T? Deserialize(JsonElement json);

    /// <summary>
    /// Десериализует <see cref="Stream"/>.
    /// </summary>
    /// <param name="utf8json">Поток json.</param>
    static abstract T? Deserialize(Stream utf8json);

    /// <summary>
    /// Десериализует <see cref="string"/>.
    /// </summary>
    /// <param name="json">Строка json.</param>
    static abstract T? Deserialize(string json);

    /// <summary>
    /// Десериализует <see cref="JsonNode"/>.
    /// </summary>
    /// <param name="node">Узел json.</param>
    static abstract T? Deserialize(JsonNode? node);

    /// <summary>
    /// Десериализует <see cref="Utf8JsonReader"/>.
    /// </summary>
    /// <param name="reader">Ридер json.</param>
    static abstract T? Deserialize(ref Utf8JsonReader reader);

    /// <summary>
    /// Десериализует <see cref="ReadOnlySpan{T}"/>.
    /// </summary>
    /// <param name="json">Строка json.</param>
    static abstract T? Deserialize(ReadOnlySpan<byte> json);

    /// <summary>
    /// Десериализует <see cref="ReadOnlySpan{T}"/>.
    /// </summary>
    /// <param name="json">Строка json.</param>
    static abstract T? Deserialize(ReadOnlySpan<char> json);

    /// <summary>
    /// Десериализует <see cref="Stream"/>.
    /// </summary>
    /// <param name="utf8json">Поток json.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    static abstract ValueTask<T?> DeserializeAsync(Stream utf8json, CancellationToken cancellationToken = default);

    /// <summary>
    /// Десериализует <see cref="Stream"/>.
    /// </summary>
    /// <param name="utf8json">Поток json.</param>
    /// <param name="topLevelValues">Указывает, следует ли перебирать только значения верхнего уровня.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    static abstract IAsyncEnumerable<T?> DeserializeAsyncEnumerable(Stream utf8json, bool topLevelValues, CancellationToken cancellationToken = default);

    /// <summary>
    /// Десериализует <see cref="Stream"/>.
    /// </summary>
    /// <param name="utf8json">Поток json.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    static abstract IAsyncEnumerable<T?> DeserializeAsyncEnumerable(Stream utf8json, CancellationToken cancellationToken = default);
}