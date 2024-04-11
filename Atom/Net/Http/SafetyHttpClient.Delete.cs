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
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public virtual ValueTask<HttpResponseMessage> DeleteAsync(Uri url, IReadOnlyDictionary<string, string>? headers, Version version, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        request.Version = version;
        request.Headers.AddRange(headers);

        return SendAsync(request, completionOption, cancellationToken);
    }

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> DeleteAsync(Uri url, IReadOnlyDictionary<string, string>? headers, Version version, HttpCompletionOption completionOption)
        => DeleteAsync(url, headers, version, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> DeleteAsync(Uri url, IReadOnlyDictionary<string, string>? headers, Version version, CancellationToken cancellationToken)
        => DeleteAsync(url, headers, version, HttpCompletionOption.ResponseContentRead, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> DeleteAsync(Uri url, IReadOnlyDictionary<string, string>? headers, Version version) => DeleteAsync(url, headers, version, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> DeleteAsync(Uri url, IReadOnlyDictionary<string, string>? headers, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        => DeleteAsync(url, headers, client.DefaultRequestVersion, completionOption, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> DeleteAsync(Uri url, IReadOnlyDictionary<string, string>? headers, HttpCompletionOption completionOption) => DeleteAsync(url, headers, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> DeleteAsync(Uri url, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
        => DeleteAsync(url, headers, HttpCompletionOption.ResponseContentRead, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> DeleteAsync(Uri url, IReadOnlyDictionary<string, string>? headers) => DeleteAsync(url, headers, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> DeleteAsync(Uri url, Version version, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        => DeleteAsync(url, default, version, completionOption, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> DeleteAsync(Uri url, Version version, HttpCompletionOption completionOption) => DeleteAsync(url, version, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> DeleteAsync(Uri url, Version version, CancellationToken cancellationToken) => DeleteAsync(url, version, HttpCompletionOption.ResponseContentRead, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> DeleteAsync(Uri url, Version version) => DeleteAsync(url, version, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> DeleteAsync(Uri url, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        => DeleteAsync(url, client.DefaultRequestVersion, completionOption, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> DeleteAsync(Uri url, HttpCompletionOption completionOption) => DeleteAsync(url, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> DeleteAsync(Uri url, CancellationToken cancellationToken) => DeleteAsync(url, client.DefaultRequestVersion, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> DeleteAsync(Uri url) => DeleteAsync(url, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> DeleteAsync<TResponse>(Uri url, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);

        request.Version = version;
        request.Headers.AddRange(headers);

        return SendAsync(request, responseTypeInfo, cancellationToken);
    }

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> DeleteAsync<TResponse>(Uri url, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo)
        => DeleteAsync(url, headers, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> DeleteAsync<TResponse>(Uri url, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => DeleteAsync(url, headers, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> DeleteAsync<TResponse>(Uri url, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo) => DeleteAsync(url, headers, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> DeleteAsync<TResponse>(Uri url, Version version, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => DeleteAsync(url, default, version, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> DeleteAsync<TResponse>(Uri url, Version version, JsonTypeInfo<TResponse> responseTypeInfo) => DeleteAsync(url, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> DeleteAsync<TResponse>(Uri url, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken) => DeleteAsync(url, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> DeleteAsync<TResponse>(Uri url, JsonTypeInfo<TResponse> responseTypeInfo) => DeleteAsync(url, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> DeleteAsyncEnumerable<TResponse>(Uri url, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);

        request.Version = version;
        request.Headers.AddRange(headers);

        return SendAsyncEnumerable(request, responseTypeInfo, cancellationToken);
    }

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> DeleteAsyncEnumerable<TResponse>(Uri url, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo)
        => DeleteAsyncEnumerable(url, headers, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> DeleteAsyncEnumerable<TResponse>(Uri url, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => DeleteAsyncEnumerable(url, headers, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> DeleteAsyncEnumerable<TResponse>(Uri url, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo)
        => DeleteAsyncEnumerable(url, headers, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> DeleteAsyncEnumerable<TResponse>(Uri url, Version version, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => DeleteAsyncEnumerable(url, default, version, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> DeleteAsyncEnumerable<TResponse>(Uri url, Version version, JsonTypeInfo<TResponse> responseTypeInfo) => DeleteAsyncEnumerable(url, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> DeleteAsyncEnumerable<TResponse>(Uri url, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => DeleteAsyncEnumerable(url, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом DELETE и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> DeleteAsyncEnumerable<TResponse>(Uri url, JsonTypeInfo<TResponse> responseTypeInfo) => DeleteAsyncEnumerable(url, responseTypeInfo, CancellationToken.None);
}