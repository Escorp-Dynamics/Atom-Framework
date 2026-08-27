using System.Net;
using Atom.Net.Https.Headers;
namespace Atom.Net.Https.Connections;

internal sealed partial class Https11Connection
{
    private async ValueTask<HttpResponseMessage> ReadResponseAsync(HttpsRequestMessage request, CancellationToken cancellationToken)
    {
        var headerToken = CreateHeaderToken(cancellationToken, out var timeoutCts);
        var headerBudget = new HeaderBudget();

        try
        {
            while (true)
            {
                var statusLine = await ReadLineAsync(headerToken).ConfigureAwait(false)
                    // Ответа не было вовсе — запрос до обработки не дошёл и может быть повторён.
                    ?? throw new HttpsIdleConnectionClosedException("Сервер закрыл соединение, не прислав строку состояния");
                AccumulateHeaderBytes(headerBudget, statusLine);

                var (version, statusCode, reasonPhrase) = ParseStatusLine(statusLine);
                if (statusCode == HttpStatusCode.SwitchingProtocols)
                    throw new NotSupportedException("HTTP upgrade/101 Switching Protocols не поддерживается минимальным H1 slice.");

                if (IsInterimStatusCode(statusCode))
                {
                    await SkipHeadersAsync(headerBudget, headerToken).ConfigureAwait(false);
                    continue;
                }

                return await BuildResponseMessageAsync(request, version, statusCode, reasonPhrase, headerBudget, headerToken, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException exception) when (timeoutCts is not null && timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Не удалось получить response headers за {options.ResponseHeadersTimeout}.", exception);
        }
        finally
        {
            timeoutCts?.Dispose();
        }
    }

    private async ValueTask<HttpResponseMessage> BuildResponseMessageAsync(HttpsRequestMessage request, Version version, HttpStatusCode statusCode, string reasonPhrase, HeaderBudget headerBudget, CancellationToken headerToken, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = new(statusCode)
        {
            Version = version,
            ReasonPhrase = reasonPhrase,
            RequestMessage = request,
        };

        try
        {
            var headersState = await ReadHeadersAsync(response, headerBudget, headerToken).ConfigureAwait(false);
            var body = await ReadResponseBodyAsync(request.Method, statusCode, headersState, cancellationToken).ConfigureAwait(false);
            response.Content = CreateResponseContent(body, headersState, options.AutoDecompression);
            ApplyConnectionDisposition(headersState);

            var completed = response;
            response = null;
            return completed;
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static HttpContent CreateResponseContent(byte[] body, ResponseHeadersState state, bool autoDecompression)
    {
        var decoded = false;

        if (autoDecompression && ContentEncodingDecoder.TryDecode(body, state.ContentEncoding, out var inflated))
        {
            body = inflated;
            decoded = true;
        }

        HttpContent content = new ByteArrayContent(body);

        foreach (var header in state.ContentHeaders)
        {
            // Распакованному телу противоречат оба заголовка: content-encoding описывает сжатие,
            // которого в нём уже нет, а content-length — длину сжатого.
            if (decoded && IsContentDescriptionHeader(header.Key)) continue;

            content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (decoded) content.Headers.ContentLength = body.Length;

        return content;
    }

    private static bool IsContentDescriptionHeader(string name)
        => string.Equals(name, "Content-Encoding", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase);

    private async ValueTask<ResponseHeadersState> ReadHeadersAsync(HttpResponseMessage response, HeaderBudget headerBudget, CancellationToken cancellationToken)
    {
        var state = new ResponseHeadersState();
        string? pending = null;

        while (true)
        {
            var line = await ReadLineAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Сервер закрыл соединение внутри блока заголовков.");
            AccumulateHeaderBytes(headerBudget, line);

            if (line.Length is 0)
            {
                if (pending is not null) ParseHeaderLine(response, state, pending);

                return state;
            }

            // ★ Строка, начинающаяся с пробела или табуляции, — ПРОДОЛЖЕНИЕ предыдущего
            // заголовка (устаревшее «складывание», RFC 7230 §3.2.4). Спецификация от него
            // отказалась, но серверы так делают до сих пор, и браузеры такие строки склеивают.
            // Мы же принимали продолжение за новый заголовок и падали на нём: у куска нет
            // двоеточия, потому что оно осталось в первой строке.
            //
            // Найдено массовым прогоном: длинная политика безопасности, разложенная сервером на
            // несколько строк, роняла весь ответ.
            if (line[0] is ' ' or '\t')
            {
                if (pending is null) throw new InvalidOperationException($"Продолжение заголовка без самого заголовка: '{line}'.");

                pending = string.Concat(pending, " ", line.Trim());
                continue;
            }

            if (pending is not null) ParseHeaderLine(response, state, pending);

            pending = line;
        }
    }

    private async ValueTask SkipHeadersAsync(HeaderBudget headerBudget, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await ReadLineAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Сервер закрыл соединение внутри блока заголовков.");
            AccumulateHeaderBytes(headerBudget, line);

            if (line.Length is 0) return;
        }
    }

    private void AccumulateHeaderBytes(HeaderBudget headerBudget, string line)
    {
        var next = checked(headerBudget.Bytes + line.Length + 2);
        if (options.MaxResponseHeadersBytes > 0 && next > options.MaxResponseHeadersBytes)
            throw new InvalidOperationException("Размер response headers превысил MaxResponseHeadersLength.");

        headerBudget.Bytes = next;
    }

    private sealed class HeaderBudget
    {
        public int Bytes { get; set; }
    }

    private static void ParseHeaderLine(HttpResponseMessage response, ResponseHeadersState state, string line)
    {
        var separator = line.IndexOf(':');
        if (separator <= 0)
            throw new InvalidOperationException($"Некорректный HTTP header line: '{line}'.");

        var name = line[..separator].Trim();
        var value = line[(separator + 1)..].Trim();

        if (string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase) && value.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            state.TransferEncodingChunked = true;

        if (string.Equals(name, "Connection", StringComparison.OrdinalIgnoreCase) && value.Contains("close", StringComparison.OrdinalIgnoreCase))
            state.ConnectionClose = true;

        if (string.Equals(name, "Content-Encoding", StringComparison.OrdinalIgnoreCase))
            state.ContentEncoding = value;

        if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedContentLength)
            && parsedContentLength >= 0)
        {
            state.ContentLength = parsedContentLength;
        }

        if (!response.Headers.TryAddWithoutValidation(name, value))
            state.ContentHeaders.Add(new KeyValuePair<string, string>(name, value));
    }

    private async ValueTask<byte[]> ReadResponseBodyAsync(HttpMethod method, HttpStatusCode statusCode, ResponseHeadersState state, CancellationToken cancellationToken)
    {
        if (!ResponseMayContainBody(method, statusCode))
        {
            state.BodyKind = ResponseBodyKind.None;
            return [];
        }

        var bodyToken = CreateBodyToken(cancellationToken, out var timeoutCts);

        try
        {
            if (state.TransferEncodingChunked)
            {
                state.BodyKind = ResponseBodyKind.Chunked;
                return await ReadChunkedBodyAsync(bodyToken).ConfigureAwait(false);
            }

            if (state.ContentLength.HasValue)
            {
                state.BodyKind = ResponseBodyKind.ContentLength;
                return await ReadFixedLengthBodyAsync(state.ContentLength.Value, bodyToken).ConfigureAwait(false);
            }

            state.BodyKind = ResponseBodyKind.CloseDelimited;
            return await ReadToEndBodyAsync(bodyToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (timeoutCts is not null && timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Не удалось получить response body за {options.ResponseBodyTimeout}.", exception);
        }
        finally
        {
            timeoutCts?.Dispose();
        }
    }

    private CancellationToken CreateHeaderToken(CancellationToken cancellationToken, out CancellationTokenSource? timeoutCts)
        => CreateTimeoutToken(options.ResponseHeadersTimeout, cancellationToken, out timeoutCts);

    private CancellationToken CreateBodyToken(CancellationToken cancellationToken, out CancellationTokenSource? timeoutCts)
        => CreateTimeoutToken(options.ResponseBodyTimeout, cancellationToken, out timeoutCts);

    private static CancellationToken CreateTimeoutToken(TimeSpan timeout, CancellationToken cancellationToken, out CancellationTokenSource? timeoutCts)
    {
        timeoutCts = null;

        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
            return cancellationToken;

        timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        return timeoutCts.Token;
    }

    private static bool ResponseMayContainBody(HttpMethod method, HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return method != HttpMethod.Head
            && !(code >= 100 && code < 200)
            && code != 204
            && code != 304;
    }

    private static bool IsInterimStatusCode(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is >= 100 and < 200 and not 101;
    }

    private static (Version Version, HttpStatusCode StatusCode, string ReasonPhrase) ParseStatusLine(string line)
    {
        if (!line.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Некорректная status line: '{line}'.");

        var firstSpace = line.IndexOf(' ');
        var secondSpace = firstSpace >= 0 ? line.IndexOf(' ', firstSpace + 1) : -1;
        if (firstSpace <= 5 || secondSpace <= firstSpace)
            throw new InvalidOperationException($"Некорректная status line: '{line}'.");

        var versionToken = line[5..firstSpace];
        var statusToken = line[(firstSpace + 1)..secondSpace];
        var reasonPhrase = secondSpace + 1 < line.Length ? line[(secondSpace + 1)..] : string.Empty;

        if (!Version.TryParse(versionToken, out var version))
            throw new InvalidOperationException($"Некорректная HTTP version: '{versionToken}'.");

        if (!int.TryParse(statusToken, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var statusCode))
            throw new InvalidOperationException($"Некорректный HTTP status code: '{statusToken}'.");

        return (version, (HttpStatusCode)statusCode, reasonPhrase);
    }
}