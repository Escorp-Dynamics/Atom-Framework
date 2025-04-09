using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;

namespace Atom.Net.Http;

public partial class SafetyHttpClient
{
    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public virtual async ValueTask<HttpResponseMessage> PutAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, url);

        request.Version = version;
        if (headers is not null) request.Headers.Add(headers);
        request.Content = content;

        return await SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PutAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, HttpCompletionOption completionOption)
        => PutAsync(url, content, headers, version, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PutAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, CancellationToken cancellationToken)
        => PutAsync(url, content, headers, version, HttpCompletionOption.ResponseContentRead, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PutAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version) => PutAsync(url, content, headers, version, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PutAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        => PutAsync(url, content, headers, client.DefaultRequestVersion, completionOption, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PutAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, HttpCompletionOption completionOption)
        => PutAsync(url, content, headers, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PutAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
        => PutAsync(url, content, headers, HttpCompletionOption.ResponseContentRead, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PutAsync(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers) => PutAsync(url, content, headers, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PutAsync(Uri url, HttpContent content, Version version, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        => PutAsync(url, content, default, version, completionOption, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PutAsync(Uri url, HttpContent content, Version version, HttpCompletionOption completionOption)
        => PutAsync(url, content, version, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PutAsync(Uri url, HttpContent content, Version version, CancellationToken cancellationToken)
        => PutAsync(url, content, version, HttpCompletionOption.ResponseContentRead, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PutAsync(Uri url, HttpContent content, Version version) => PutAsync(url, content, version, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PutAsync(Uri url, HttpContent content, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        => PutAsync(url, content, client.DefaultRequestVersion, completionOption, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="completionOption">Режим чтения ответа.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PutAsync(Uri url, HttpContent content, HttpCompletionOption completionOption) => PutAsync(url, content, completionOption, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PutAsync(Uri url, HttpContent content, CancellationToken cancellationToken) => PutAsync(url, content, client.DefaultRequestVersion, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа.
    /// </summary>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Данные запроса.</param>
    /// <returns>Данные ответа.</returns>
    public ValueTask<HttpResponseMessage> PutAsync(Uri url, HttpContent content) => PutAsync(url, content, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public async ValueTask<TResponse?> PutAsync<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, url);

        request.Version = version;
        if (headers is not null) request.Headers.Add(headers);
        request.Content = content;

        return await SendAsync(request, responseTypeInfo, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PutAsync<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo)
        => PutAsync(url, content, headers, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PutAsync<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => PutAsync(url, content, headers, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PutAsync<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo)
        => PutAsync(url, content, headers, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PutAsync<TResponse>(Uri url, HttpContent content, Version version, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => PutAsync(url, content, default, version, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PutAsync<TResponse>(Uri url, HttpContent content, Version version, JsonTypeInfo<TResponse> responseTypeInfo) => PutAsync(url, content, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PutAsync<TResponse>(Uri url, HttpContent content, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => PutAsync(url, content, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа JSON, десериализованные в объект.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в объект.</returns>
    public ValueTask<TResponse?> PutAsync<TResponse>(Uri url, HttpContent content, JsonTypeInfo<TResponse> responseTypeInfo) => PutAsync(url, content, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public async IAsyncEnumerable<TResponse?> PutAsyncEnumerable<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, url);

        request.Version = version;
        if (headers is not null) request.Headers.Add(headers);
        request.Content = content;

        await foreach (var item in SendAsyncEnumerable(request, responseTypeInfo, cancellationToken).ConfigureAwait(false)) yield return item;
        yield break;
    }

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PutAsyncEnumerable<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, Version version, JsonTypeInfo<TResponse> responseTypeInfo)
        => PutAsyncEnumerable(url, content, headers, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PutAsyncEnumerable<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => PutAsyncEnumerable(url, content, headers, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="headers">Заголовки запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PutAsyncEnumerable<TResponse>(Uri url, HttpContent content, IReadOnlyDictionary<string, string>? headers, JsonTypeInfo<TResponse> responseTypeInfo)
        => PutAsyncEnumerable(url, content, headers, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PutAsyncEnumerable<TResponse>(Uri url, HttpContent content, Version version, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => PutAsyncEnumerable(url, content, default, version, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="version">Версия запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PutAsyncEnumerable<TResponse>(Uri url, HttpContent content, Version version, JsonTypeInfo<TResponse> responseTypeInfo)
        => PutAsyncEnumerable(url, content, version, responseTypeInfo, CancellationToken.None);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PutAsyncEnumerable<TResponse>(Uri url, HttpContent content, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken cancellationToken)
        => PutAsyncEnumerable(url, content, client.DefaultRequestVersion, responseTypeInfo, cancellationToken);

    /// <summary>
    /// Осуществляет запрос методом PUT и возвращает данные ответа JSON, десериализованные в коллекцию объектов.
    /// </summary>
    /// <typeparam name="TResponse">Тип данных ответа.</typeparam>
    /// <param name="url">Адрес запроса.</param>
    /// <param name="content">Контент запроса.</param>
    /// <param name="responseTypeInfo">Метаданные типа ответа.</param>
    /// <returns>Данные ответа JSON, десериализованные в коллекцию объектов.</returns>
    public IAsyncEnumerable<TResponse?> PutAsyncEnumerable<TResponse>(Uri url, HttpContent content, JsonTypeInfo<TResponse> responseTypeInfo)
        => PutAsyncEnumerable(url, content, responseTypeInfo, CancellationToken.None);

}