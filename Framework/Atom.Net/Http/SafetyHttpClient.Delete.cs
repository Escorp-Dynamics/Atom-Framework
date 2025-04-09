using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;

namespace Atom.Net.Http;

public partial class SafetyHttpClient
{
    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual async ValueTask<SafetyHttpResponseMessage> DeleteAsync(UrlBuilder url, IReadOnlyDictionary<string, string> headers, Version version, CancellationToken cancellationToken)
    {
        var rb = HttpRequestBuilder.Rent()
            .WithMethod(HttpMethod.Delete)
            .WithUrl(url)
            .WithVersion(version)
            .WithHeaders(headers);

        using var request = rb.Build();
        HttpRequestBuilder.Return(rb);

        return await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <returns>Данные ответа.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage> DeleteAsync(UrlBuilder url, IReadOnlyDictionary<string, string> headers, Version version)
        => DeleteAsync(url, headers, version, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage> DeleteAsync(UrlBuilder url, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
        => DeleteAsync(url, headers, DefaultRequestVersion, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <returns>Данные ответа.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage> DeleteAsync(UrlBuilder url, IReadOnlyDictionary<string, string> headers) => DeleteAsync(url, headers, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<SafetyHttpResponseMessage> DeleteAsync(UrlBuilder url, Version version, CancellationToken cancellationToken)
    {
        var rb = HttpRequestBuilder.Rent()
            .WithMethod(HttpMethod.Delete)
            .WithUrl(url)
            .WithVersion(version);

        using var request = rb.Build();
        HttpRequestBuilder.Return(rb);

        return await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <returns>Данные ответа.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage> DeleteAsync(UrlBuilder url, Version version) => DeleteAsync(url, version, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage> DeleteAsync(UrlBuilder url, CancellationToken cancellationToken) => DeleteAsync(url, DefaultRequestVersion, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <returns>Данные ответа.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage> DeleteAsync(UrlBuilder url) => DeleteAsync(url, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<SafetyHttpResponseMessage<T>> DeleteAsync<T>(UrlBuilder url, IReadOnlyDictionary<string, string> headers, Version version, JsonTypeInfo<T> responseTypeInfo, CancellationToken cancellationToken)
    {
        var rb = HttpRequestBuilder.Rent()
            .WithMethod(HttpMethod.Delete)
            .WithUrl(url)
            .WithVersion(version)
            .WithHeaders(headers);

        using var request = rb.Build();
        HttpRequestBuilder.Return(rb);

        return await SendAsync(request, responseTypeInfo, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage<T>> DeleteAsync<T>(UrlBuilder url, IReadOnlyDictionary<string, string> headers, Version version, JsonTypeInfo<T> responseTypeInfo)
        => DeleteAsync(url, headers, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage<T>> DeleteAsync<T>(UrlBuilder url, IReadOnlyDictionary<string, string> headers, JsonTypeInfo<T> responseTypeInfo, CancellationToken cancellationToken)
        => DeleteAsync(url, headers, DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage<T>> DeleteAsync<T>(UrlBuilder url, IReadOnlyDictionary<string, string> headers, JsonTypeInfo<T> responseTypeInfo) => DeleteAsync(url, headers, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<SafetyHttpResponseMessage<T>> DeleteAsync<T>(UrlBuilder url, Version version, JsonTypeInfo<T> responseTypeInfo, CancellationToken cancellationToken)
    {
        var rb = HttpRequestBuilder.Rent()
            .WithMethod(HttpMethod.Delete)
            .WithUrl(url)
            .WithVersion(version);

        using var request = rb.Build();
        HttpRequestBuilder.Return(rb);

        return await SendAsync(request, responseTypeInfo, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage<T>> DeleteAsync<T>(UrlBuilder url, Version version, JsonTypeInfo<T> responseTypeInfo) => DeleteAsync(url, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage<T>> DeleteAsync<T>(UrlBuilder url, JsonTypeInfo<T> responseTypeInfo, CancellationToken cancellationToken) => DeleteAsync(url, DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage<T>> DeleteAsync<T>(UrlBuilder url, JsonTypeInfo<T> responseTypeInfo) => DeleteAsync(url, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async IAsyncEnumerable<T?> DeleteAsyncEnumerable<T>(UrlBuilder url, IReadOnlyDictionary<string, string> headers, Version version, JsonTypeInfo<T> responseTypeInfo, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rb = HttpRequestBuilder.Rent()
            .WithUrl(url)
            .WithVersion(version)
            .WithHeaders(headers);

        using var request = rb.Build();
        HttpRequestBuilder.Return(rb);

        await foreach (var item in SendAsyncEnumerable(request, responseTypeInfo, cancellationToken).ConfigureAwait(false)) yield return item;
    }

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IAsyncEnumerable<T?> DeleteAsyncEnumerable<T>(UrlBuilder url, IReadOnlyDictionary<string, string> headers, Version version, JsonTypeInfo<T> responseTypeInfo)
        => DeleteAsyncEnumerable(url, headers, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IAsyncEnumerable<T?> DeleteAsyncEnumerable<T>(UrlBuilder url, IReadOnlyDictionary<string, string> headers, JsonTypeInfo<T> responseTypeInfo, CancellationToken cancellationToken)
        => DeleteAsyncEnumerable(url, headers, DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IAsyncEnumerable<T?> DeleteAsyncEnumerable<T>(UrlBuilder url, IReadOnlyDictionary<string, string> headers, JsonTypeInfo<T> responseTypeInfo)
        => DeleteAsyncEnumerable(url, headers, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async IAsyncEnumerable<T?> DeleteAsyncEnumerable<T>(UrlBuilder url, Version version, JsonTypeInfo<T> responseTypeInfo, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rb = HttpRequestBuilder.Rent()
            .WithUrl(url)
            .WithVersion(version);

        using var request = rb.Build();
        HttpRequestBuilder.Return(rb);

        await foreach (var item in SendAsyncEnumerable(request, responseTypeInfo, cancellationToken).ConfigureAwait(false)) yield return item;
    }

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IAsyncEnumerable<T?> DeleteAsyncEnumerable<T>(UrlBuilder url, Version version, JsonTypeInfo<T> responseTypeInfo) => DeleteAsyncEnumerable(url, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IAsyncEnumerable<T?> DeleteAsyncEnumerable<T>(UrlBuilder url, JsonTypeInfo<T> responseTypeInfo, CancellationToken cancellationToken)
        => DeleteAsyncEnumerable(url, DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IAsyncEnumerable<T?> DeleteAsyncEnumerable<T>(UrlBuilder url, JsonTypeInfo<T> responseTypeInfo) => DeleteAsyncEnumerable(url, responseTypeInfo, CancellationToken.None);
}