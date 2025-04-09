using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;

namespace Atom.Net.Http;

public partial class SafetyHttpClient
{
    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual async ValueTask<SafetyHttpResponseMessage> GetAsync(UrlBuilder url, IReadOnlyDictionary<string, string> headers, Version version, CancellationToken cancellationToken)
    {
        var rb = HttpRequestBuilder.Rent()
            .WithUrl(url)
            .WithVersion(version)
            .WithHeaders(headers);

        using var request = rb.Build();
        HttpRequestBuilder.Return(rb);

        return await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <returns>Данные ответа.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage> GetAsync(UrlBuilder url, IReadOnlyDictionary<string, string> headers, Version version)
        => GetAsync(url, headers, version, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage> GetAsync(UrlBuilder url, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
        => GetAsync(url, headers, HttpVersion.Version30, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <returns>Данные ответа.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage> GetAsync(UrlBuilder url, IReadOnlyDictionary<string, string> headers) => GetAsync(url, headers, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<SafetyHttpResponseMessage> GetAsync(UrlBuilder url, Version version, CancellationToken cancellationToken)
    {
        var rb = HttpRequestBuilder.Rent()
            .WithUrl(url)
            .WithVersion(version);

        using var request = rb.Build();
        HttpRequestBuilder.Return(rb);

        return await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <returns>Данные ответа.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage> GetAsync(UrlBuilder url, Version version) => GetAsync(url, version, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage> GetAsync(UrlBuilder url, CancellationToken cancellationToken) => GetAsync(url, DefaultRequestVersion, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <returns>Данные ответа.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage> GetAsync(UrlBuilder url) => GetAsync(url, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<SafetyHttpResponseMessage<T>> GetAsync<T>(UrlBuilder url, IReadOnlyDictionary<string, string> headers, Version version, JsonTypeInfo<T> responseTypeInfo, CancellationToken cancellationToken)
    {
        var rb = HttpRequestBuilder.Rent()
            .WithUrl(url)
            .WithVersion(version)
            .WithHeaders(headers);

        using var request = rb.Build();
        HttpRequestBuilder.Return(rb);

        return await SendAsync(request, responseTypeInfo, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage<T>> GetAsync<T>(UrlBuilder url, IReadOnlyDictionary<string, string> headers, Version version, JsonTypeInfo<T> responseTypeInfo)
        => GetAsync(url, headers, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage<T>> GetAsync<T>(UrlBuilder url, IReadOnlyDictionary<string, string> headers, JsonTypeInfo<T> responseTypeInfo, CancellationToken cancellationToken)
        => GetAsync(url, headers, DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage<T>> GetAsync<T>(UrlBuilder url, IReadOnlyDictionary<string, string> headers, JsonTypeInfo<T> responseTypeInfo) => GetAsync(url, headers, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<SafetyHttpResponseMessage<T>> GetAsync<T>(UrlBuilder url, Version version, JsonTypeInfo<T> responseTypeInfo, CancellationToken cancellationToken)
    {
        var rb = HttpRequestBuilder.Rent()
            .WithUrl(url)
            .WithVersion(version);

        using var request = rb.Build();
        HttpRequestBuilder.Return(rb);

        return await SendAsync(request, responseTypeInfo, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage<T>> GetAsync<T>(UrlBuilder url, Version version, JsonTypeInfo<T> responseTypeInfo) => GetAsync(url, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage<T>> GetAsync<T>(UrlBuilder url, JsonTypeInfo<T> responseTypeInfo, CancellationToken cancellationToken) => GetAsync(url, DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<SafetyHttpResponseMessage<T>> GetAsync<T>(UrlBuilder url, JsonTypeInfo<T> responseTypeInfo) => GetAsync(url, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async IAsyncEnumerable<T?> GetAsyncEnumerable<T>(UrlBuilder url, IReadOnlyDictionary<string, string> headers, Version version, JsonTypeInfo<T> responseTypeInfo, [EnumeratorCancellation] CancellationToken cancellationToken)
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
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IAsyncEnumerable<T?> GetAsyncEnumerable<T>(UrlBuilder url, IReadOnlyDictionary<string, string> headers, Version version, JsonTypeInfo<T> responseTypeInfo)
        => GetAsyncEnumerable(url, headers, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IAsyncEnumerable<T?> GetAsyncEnumerable<T>(UrlBuilder url, IReadOnlyDictionary<string, string> headers, JsonTypeInfo<T> responseTypeInfo, CancellationToken cancellationToken)
        => GetAsyncEnumerable(url, headers, DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IAsyncEnumerable<T?> GetAsyncEnumerable<T>(UrlBuilder url, IReadOnlyDictionary<string, string> headers, JsonTypeInfo<T> responseTypeInfo)
        => GetAsyncEnumerable(url, headers, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async IAsyncEnumerable<T?> GetAsyncEnumerable<T>(UrlBuilder url, Version version, JsonTypeInfo<T> responseTypeInfo, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rb = HttpRequestBuilder.Rent()
            .WithUrl(url)
            .WithVersion(version);

        using var request = rb.Build();
        HttpRequestBuilder.Return(rb);

        await foreach (var item in SendAsyncEnumerable(request, responseTypeInfo, cancellationToken).ConfigureAwait(false)) yield return item;
    }

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IAsyncEnumerable<T?> GetAsyncEnumerable<T>(UrlBuilder url, Version version, JsonTypeInfo<T> responseTypeInfo) => GetAsyncEnumerable(url, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IAsyncEnumerable<T?> GetAsyncEnumerable<T>(UrlBuilder url, JsonTypeInfo<T> responseTypeInfo, CancellationToken cancellationToken)
        => GetAsyncEnumerable(url, DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="T">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IAsyncEnumerable<T?> GetAsyncEnumerable<T>(UrlBuilder url, JsonTypeInfo<T> responseTypeInfo) => GetAsyncEnumerable(url, responseTypeInfo, CancellationToken.None);
}