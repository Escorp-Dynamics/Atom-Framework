#pragma warning disable IDE0046

using Atom.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Web;

namespace Atom.Net.Http;

/// <summary>
/// Представляет расширения для <see cref="Http"/>.
/// </summary>
public static class HttpExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string KeySelector(KeyValuePair<string, IEnumerable<string>> item) => item.Key;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ValueSelector(KeyValuePair<string, IEnumerable<string>> item) => item.Value.Join();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object? TryGetNumber(JsonElement element)
    {
        if (element.TryGetByte(out var b)) return b;
        if (element.TryGetSByte(out var sb)) return sb;
        if (element.TryGetInt16(out var s)) return s;
        if (element.TryGetUInt16(out var us)) return us;
        if (element.TryGetInt32(out var i)) return i;
        if (element.TryGetUInt32(out var ui)) return ui;
        if (element.TryGetInt64(out var l)) return l;
        if (element.TryGetUInt64(out var ul)) return ul;
        if (element.TryGetSingle(out var f)) return f;
        if (element.TryGetDouble(out var d)) return d;
        if (element.TryGetDecimal(out var dc)) return dc;
        if (element.TryGetGuid(out var g)) return g;
        if (element.TryGetDateTime(out var dt)) return dt;
        if (element.TryGetDateTimeOffset(out var dto)) return dto;
        return element;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T? Deserialize<T>(T? data)
    {
        if (data is not IReadOnlyDictionary<string, object> items) return data;

        var form = new Dictionary<string, object?>();

        foreach (var (key, value) in items)
        {
            if (value is not JsonElement element)
            {
                form.Add(key, value);
                continue;
            }

            var v = element.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.False => false,
                JsonValueKind.True => true,
                JsonValueKind.Number => TryGetNumber(element),
                JsonValueKind.String => element.GetString(),
                _ => value,
            };

            form.Add(key, v);
        }

        return (T?)(form as IReadOnlyDictionary<string, object?>);
    }

    /// <summary>
    /// Возвращает словарь всех заголовков запроса.
    /// </summary>
    /// <param name="request">Данные запроса.</param>
    /// <returns>Словарь всех заголовков запроса.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IDictionary<string, string>? GetHeaders(this HttpRequestMessage? request) => request?.Headers
        .ToDictionary(KeySelector, ValueSelector, StringComparer.OrdinalIgnoreCase)
        .AddRange(request.Content?.Headers.ToDictionary(KeySelector, ValueSelector, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Возвращает словарь всех заголовков ответа.
    /// </summary>
    /// <param name="response">Данные ответа.</param>
    /// <returns>Словарь всех заголовков ответа.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IDictionary<string, string>? GetHeaders(this HttpResponseMessage? response) => response?.Headers
        .ToDictionary(KeySelector, ValueSelector, StringComparer.OrdinalIgnoreCase)
        .AddRange(response.Content.Headers.ToDictionary(KeySelector, ValueSelector, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Преобразует содержимое ответа JSON в объект.
    /// </summary>
    /// <typeparam name="T">Тип объекта.</typeparam>
    /// <param name="content">Контент ответа.</param>
    /// <param name="typeInfo">Метаданные типа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Преобразованный объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask<T?> AsJsonAsync<T>(this HttpContent? content, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        if (content is null) return default;

        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            var data = await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken).ConfigureAwait(false);
            return Deserialize(data);
        }
    }

    /// <summary>
    /// Преобразует содержимое ответа JSON в объект.
    /// </summary>
    /// <typeparam name="T">Тип объекта.</typeparam>
    /// <param name="content">Контент ответа.</param>
    /// <param name="typeInfo">Метаданные типа.</param>
    /// <returns>Преобразованный объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<T?> AsJsonAsync<T>(this HttpContent? content, JsonTypeInfo<T> typeInfo) => content.AsJsonAsync(typeInfo, CancellationToken.None);

    /// <summary>
    /// Преобразует содержимое ответа JSON в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип объекта.</typeparam>
    /// <param name="content">Контент ответа.</param>
    /// <param name="typeInfo">Метаданные типа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Преобразованная коллекция объектов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async IAsyncEnumerable<T?> AsJsonAsyncEnumerable<T>(this HttpContent? content, JsonTypeInfo<T> typeInfo, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (content is null) yield break;

        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable(stream, typeInfo, cancellationToken).ConfigureAwait(false))
                yield return Deserialize(item);
        }
    }

    /// <summary>
    /// Преобразует содержимое ответа JSON в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип объекта.</typeparam>
    /// <param name="content">Контент ответа.</param>
    /// <param name="typeInfo">Метаданные типа.</param>
    /// <returns>Преобразованная коллекция объектов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IAsyncEnumerable<T?> AsJsonAsyncEnumerable<T>(this HttpContent? content, JsonTypeInfo<T> typeInfo) => content.AsJsonAsyncEnumerable(typeInfo, CancellationToken.None);

    /// <summary>
    /// Преобразует <see cref="HttpRequestHeaders"/> в простое представление заголовков.
    /// </summary>
    /// <param name="headers">Исходные заголовки</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<KeyValuePair<string, string>> AsSimple(this HttpRequestHeaders? headers)
    {
        if (headers is null) yield break;

        foreach (var header in headers)
            yield return new KeyValuePair<string, string>(header.Key, string.Join(", ", header.Value));
    }

    /// <summary>
    /// Добавляет несколько элементов в словарь.
    /// </summary>
    /// <param name="headers">Исходный словарь.</param>
    /// <param name="items">Добавляемые элементы.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HttpRequestHeaders? Add(this HttpRequestHeaders? headers, IReadOnlyDictionary<string, string> items)
    {
        if (headers is null || items is null) return headers;

        foreach (var item in items)
            headers.TryAddWithoutValidation(item.Key, item.Value);

        return headers;
    }

    /// <summary>
    /// Преобразует body формы запроса в <see cref="FormUrlEncodedContent"/>.
    /// </summary>
    /// <param name="body">Исходная строка body формы запроса.</param>
    /// <returns>Экземпляр <see cref="FormUrlEncodedContent"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FormUrlEncodedContent AsFormUrlEncodedContent([NotNull] this string body) => new(body.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(x => x.Split('=', 2, StringSplitOptions.TrimEntries))
        .ToDictionary(x => HttpUtility.UrlDecode(x[0]), x => HttpUtility.UrlDecode(x[1]), StringComparer.OrdinalIgnoreCase));
}