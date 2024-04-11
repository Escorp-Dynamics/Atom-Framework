using System.Text.Json.Serialization.Metadata;

namespace Atom.Net.Http;

public partial class SafetyHttpClient
{
    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public virtual ValueTask<HttpResponseMessage> PostAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        request.Version = version;
        request.Headers.AddRange(headers);
        request.Content = content;

        return SendAsync(request, completionOption, cancellationToken);
    }

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PostAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, HttpCompletionOption completionOption)
        => PostAsync(url, content, headers, version, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PostAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, CancellationToken cancellationToken)
        => PostAsync(url, content, headers, version, HttpCompletionOption.ResponseContentRead, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PostAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version) => PostAsync(url, content, headers, version, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PostAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        => PostAsync(url, content, headers, client.DefaultRequestVersion, completionOption, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PostAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, HttpCompletionOption completionOption)
        => PostAsync(url, content, headers, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PostAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
        => PostAsync(url, content, headers, HttpCompletionOption.ResponseContentRead, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PostAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers) => PostAsync(url, content, headers, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PostAsync(Uri url, HttpContent content, Version version, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        => PostAsync(url, content, default, version, completionOption, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PostAsync(Uri url, HttpContent content, Version version, HttpCompletionOption completionOption)
        => PostAsync(url, content, version, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PostAsync(Uri url, HttpContent content, Version version, CancellationToken cancellationToken)
        => PostAsync(url, content, version, HttpCompletionOption.ResponseContentRead, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PostAsync(Uri url, HttpContent content, Version version) => PostAsync(url, content, version, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PostAsync(Uri url, HttpContent content, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        => PostAsync(url, content, client.DefaultRequestVersion, completionOption, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PostAsync(Uri url, HttpContent content, HttpCompletionOption completionOption) => PostAsync(url, content, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PostAsync(Uri url, HttpContent content, CancellationToken cancellationToken) => PostAsync(url, content, client.DefaultRequestVersion, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PostAsync(Uri url, HttpContent content) => PostAsync(url, content, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PostAsync<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        request.Version = version;
        request.Headers.AddRange(headers);
        request.Content = content;

        return SendAsync(request, responseTypeInfo, cancellationToken);
    }

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PostAsync<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo)
        => PostAsync(url, content, headers, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PostAsync<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => PostAsync(url, content, headers, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PostAsync<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo)
        => PostAsync(url, content, headers, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PostAsync<TResponse>(Uri url, HttpContent content, Version version, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => PostAsync(url, content, default, version, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PostAsync<TResponse>(Uri url, HttpContent content, Version version, JsonTypeInfo<TResponse> responseTypeInfo)
        => PostAsync(url, content, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PostAsync<TResponse>(Uri url, HttpContent content, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => PostAsync(url, content, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PostAsync<TResponse>(Uri url, HttpContent content, JsonTypeInfo<TResponse> responseTypeInfo) => PostAsync(url, content, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PostAsyncEnumerable<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        request.Version = version;
        request.Headers.AddRange(headers);
        request.Content = content;

        return SendAsyncEnumerable(request, responseTypeInfo, cancellationToken);
    }

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PostAsyncEnumerable<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo)
        => PostAsyncEnumerable(url, content, headers, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PostAsyncEnumerable<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => PostAsyncEnumerable(url, content, headers, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PostAsyncEnumerable<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo)
        => PostAsyncEnumerable(url, content, headers, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PostAsyncEnumerable<TResponse>(Uri url, HttpContent content, Version version, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => PostAsyncEnumerable(url, content, default, version, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PostAsyncEnumerable<TResponse>(Uri url, HttpContent content, Version version, JsonTypeInfo<TResponse> responseTypeInfo)
        => PostAsyncEnumerable(url, content, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PostAsyncEnumerable<TResponse>(Uri url, HttpContent content, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => PostAsyncEnumerable(url, content, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом POST и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PostAsyncEnumerable<TResponse>(Uri url, HttpContent content, JsonTypeInfo<TResponse> responseTypeInfo)
        => PostAsyncEnumerable(url, content, responseTypeInfo, CancellationToken.None);
}