using System.Text.Json.Serialization.Metadata;

namespace Atom.Net.Http;

public partial class SafetyHttpClient
{
    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public virtual ValueTask<HttpResponseMessage> PatchAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, url);

        request.Version = version;
        request.Headers.AddRange(headers);
        request.Content = content;

        return SendAsync(request, completionOption, cancellationToken);
    }

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PatchAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, HttpCompletionOption completionOption)
        => PatchAsync(url, content, headers, version, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PatchAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, CancellationToken cancellationToken)
        => PatchAsync(url, content, headers, version, HttpCompletionOption.ResponseContentRead, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PatchAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version) => PatchAsync(url, content, headers, version, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PatchAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        => PatchAsync(url, content, headers, client.DefaultRequestVersion, completionOption, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PatchAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, HttpCompletionOption completionOption)
        => PatchAsync(url, content, headers, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PatchAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
        => PatchAsync(url, content, headers, HttpCompletionOption.ResponseContentRead, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PatchAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers) => PatchAsync(url, content, headers, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PatchAsync(Uri url, HttpContent content, Version version, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        => PatchAsync(url, content, default, version, completionOption, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PatchAsync(Uri url, HttpContent content, Version version, HttpCompletionOption completionOption)
        => PatchAsync(url, content, version, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PatchAsync(Uri url, HttpContent content, Version version, CancellationToken cancellationToken)
        => PatchAsync(url, content, version, HttpCompletionOption.ResponseContentRead, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PatchAsync(Uri url, HttpContent content, Version version) => PatchAsync(url, content, version, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PatchAsync(Uri url, HttpContent content, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        => PatchAsync(url, content, client.DefaultRequestVersion, completionOption, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PatchAsync(Uri url, HttpContent content, HttpCompletionOption completionOption) => PatchAsync(url, content, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PatchAsync(Uri url, HttpContent content, CancellationToken cancellationToken) => PatchAsync(url, content, client.DefaultRequestVersion, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PatchAsync(Uri url, HttpContent content) => PatchAsync(url, content, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PatchAsync<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, url);

        request.Version = version;
        request.Headers.AddRange(headers);
        request.Content = content;

        return SendAsync(request, responseTypeInfo, cancellationToken);
    }

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PatchAsync<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo)
        => PatchAsync(url, content, headers, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PatchAsync<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => PatchAsync(url, content, headers, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PatchAsync<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo)
        => PatchAsync(url, content, headers, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PatchAsync<TResponse>(Uri url, HttpContent content, Version version, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => PatchAsync(url, content, default, version, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PatchAsync<TResponse>(Uri url, HttpContent content, Version version, JsonTypeInfo<TResponse> responseTypeInfo) => PatchAsync(url, content, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PatchAsync<TResponse>(Uri url, HttpContent content, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => PatchAsync(url, content, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PatchAsync<TResponse>(Uri url, HttpContent content, JsonTypeInfo<TResponse> responseTypeInfo) => PatchAsync(url, content, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PatchAsyncEnumerable<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, url);

        request.Version = version;
        request.Headers.AddRange(headers);
        request.Content = content;

        return SendAsyncEnumerable(request, responseTypeInfo, cancellationToken);
    }

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PatchAsyncEnumerable<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo)
        => PatchAsyncEnumerable(url, content, headers, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PatchAsyncEnumerable<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => PatchAsyncEnumerable(url, content, headers, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PatchAsyncEnumerable<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo)
        => PatchAsyncEnumerable(url, content, headers, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PatchAsyncEnumerable<TResponse>(Uri url, HttpContent content, Version version, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => PatchAsyncEnumerable(url, content, default, version, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PatchAsyncEnumerable<TResponse>(Uri url, HttpContent content, Version version, JsonTypeInfo<TResponse> responseTypeInfo)
        => PatchAsyncEnumerable(url, content, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PatchAsyncEnumerable<TResponse>(Uri url, HttpContent content, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => PatchAsyncEnumerable(url, content, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PATCH и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PatchAsyncEnumerable<TResponse>(Uri url, HttpContent content, JsonTypeInfo<TResponse> responseTypeInfo)
        => PatchAsyncEnumerable(url, content, responseTypeInfo, CancellationToken.None);
}