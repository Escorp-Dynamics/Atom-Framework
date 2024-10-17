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
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public virtual async ValueTask<HttpResponseMessage> GetAsync(Uri url, IReadOnlyDictionary<string, string>? headers, Version version, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        request.Version = version;
        if (headers is not null) request.Headers.Add(headers);

        return await SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> GetAsync(Uri url, IReadOnlyDictionary<string, string>? headers, Version version, HttpCompletionOption completionOption)
        => GetAsync(url, headers, version, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> GetAsync(Uri url, IReadOnlyDictionary<string, string>? headers, Version version, CancellationToken cancellationToken)
        => GetAsync(url, headers, version, HttpCompletionOption.ResponseContentRead, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> GetAsync(Uri url, IReadOnlyDictionary<string, string>? headers, Version version) => GetAsync(url, headers, version, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> GetAsync(Uri url, IReadOnlyDictionary<string, string>? headers, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        => GetAsync(url, headers, client.DefaultRequestVersion, completionOption, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> GetAsync(Uri url, IReadOnlyDictionary<string, string>? headers, HttpCompletionOption completionOption) => GetAsync(url, headers, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> GetAsync(Uri url, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
        => GetAsync(url, headers, HttpCompletionOption.ResponseContentRead, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> GetAsync(Uri url, IReadOnlyDictionary<string, string>? headers) => GetAsync(url, headers, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> GetAsync(Uri url, Version version, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        => GetAsync(url, default, version, completionOption, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> GetAsync(Uri url, Version version, HttpCompletionOption completionOption) => GetAsync(url, version, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> GetAsync(Uri url, Version version, CancellationToken cancellationToken) => GetAsync(url, version, HttpCompletionOption.ResponseContentRead, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> GetAsync(Uri url, Version version) => GetAsync(url, version, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> GetAsync(Uri url, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        => GetAsync(url, client.DefaultRequestVersion, completionOption, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> GetAsync(Uri url, HttpCompletionOption completionOption) => GetAsync(url, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> GetAsync(Uri url, CancellationToken cancellationToken) => GetAsync(url, client.DefaultRequestVersion, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> GetAsync(Uri url) => GetAsync(url, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public async ValueTask<TResponse?> GetAsync<TResponse>(Uri url, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        request.Version = version;
        if (headers is not null) request.Headers.Add(headers);

        return await SendAsync(request, responseTypeInfo, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> GetAsync<TResponse>(Uri url, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo)
        => GetAsync(url, headers, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> GetAsync<TResponse>(Uri url, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => GetAsync(url, headers, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> GetAsync<TResponse>(Uri url, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo) => GetAsync(url, headers, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> GetAsync<TResponse>(Uri url, Version version, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => GetAsync(url, default, version, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> GetAsync<TResponse>(Uri url, Version version, JsonTypeInfo<TResponse> responseTypeInfo) => GetAsync(url, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> GetAsync<TResponse>(Uri url, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken) => GetAsync(url, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> GetAsync<TResponse>(Uri url, JsonTypeInfo<TResponse> responseTypeInfo) => GetAsync(url, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public async IAsyncEnumerable<TResponse?> GetAsyncEnumerable<TResponse>(Uri url, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        request.Version = version;
        if (headers is not null) request.Headers.Add(headers);

        await foreach (var item in SendAsyncEnumerable(request, responseTypeInfo, cancellationToken).ConfigureAwait(false)) yield return item;
        yield break;
    }

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> GetAsyncEnumerable<TResponse>(Uri url, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo)
        => GetAsyncEnumerable(url, headers, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> GetAsyncEnumerable<TResponse>(Uri url, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => GetAsyncEnumerable(url, headers, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> GetAsyncEnumerable<TResponse>(Uri url, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo)
        => GetAsyncEnumerable(url, headers, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> GetAsyncEnumerable<TResponse>(Uri url, Version version, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => GetAsyncEnumerable(url, default, version, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> GetAsyncEnumerable<TResponse>(Uri url, Version version, JsonTypeInfo<TResponse> responseTypeInfo) => GetAsyncEnumerable(url, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> GetAsyncEnumerable<TResponse>(Uri url, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => GetAsyncEnumerable(url, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом GET и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> GetAsyncEnumerable<TResponse>(Uri url, JsonTypeInfo<TResponse> responseTypeInfo) => GetAsyncEnumerable(url, responseTypeInfo, CancellationToken.None);
}