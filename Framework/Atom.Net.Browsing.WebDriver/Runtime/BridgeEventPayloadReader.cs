using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Atom.Net.Browsing.WebDriver.Protocol;
using Atom.Net.Https;
using Atom.Text;

namespace Atom.Net.Browsing.WebDriver;

internal static class BridgeEventPayloadReader
{
    internal static bool TryReadScriptError(BridgeMessage message, out string details)
    {
        details = string.Empty;
        if (!TryGetPayloadObject(message, BridgeEvent.ScriptError, out var payload))
            return false;

        details = BuildScriptErrorDetails(payload);
        return true;
    }

    private static string BuildScriptErrorDetails(JsonElement payload)
    {
        var builder = new ValueStringBuilder(128);

        try
        {
            AppendScriptErrorMessage(ref builder, ReadString(payload, "message"));
            AppendScriptErrorField(ref builder, "kind", ReadString(payload, "kind"));
            AppendScriptErrorField(ref builder, "file", ReadString(payload, "filename"));
            AppendScriptErrorLocation(ref builder, payload);

            return builder.Length == 0 ? payload.GetRawText() : builder.ToString();
        }
        finally
        {
            builder.Dispose();
        }
    }

    private static void AppendScriptErrorMessage(ref ValueStringBuilder builder, string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(errorMessage))
            builder.Append(errorMessage);
    }

    private static void AppendScriptErrorField(ref ValueStringBuilder builder, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        AppendScriptErrorSeparator(ref builder);
        builder.Append(name);
        builder.Append('=');
        builder.Append(value);
    }

    private static void AppendScriptErrorLocation(ref ValueStringBuilder builder, JsonElement payload)
    {
        if (!TryReadScriptErrorLocation(payload, out var line, out var column))
            return;

        AppendScriptErrorSeparator(ref builder);
        builder.Append("line=");
        builder.Append(line);

        if (column is null)
            return;

        builder.Append(", column=");
        builder.Append(column.Value);
    }

    private static bool TryReadScriptErrorLocation(JsonElement payload, out int line, out int? column)
    {
        line = 0;
        column = null;
        if (!payload.TryGetProperty("line", out var lineElement) || !lineElement.TryGetInt32(out line))
            return false;

        if (payload.TryGetProperty("column", out var columnElement) && columnElement.TryGetInt32(out var value))
            column = value;

        return true;
    }

    private static void AppendScriptErrorSeparator(ref ValueStringBuilder builder)
    {
        if (builder.Length > 0)
            builder.Append("; ");
    }

    internal static bool TryReadConsoleMessage(BridgeMessage message, IFrame frame, out ConsoleMessageEventArgs args)
    {
        args = default!;

        if (!TryGetPayloadObject(message, BridgeEvent.ConsoleMessage, out var payload))
        {
            return false;
        }

        var values = ReadValues(payload, "args");
        var text = ReadString(payload, "message");
        if (string.IsNullOrWhiteSpace(text) && values.Length > 0)
        {
            text = string.Join(' ', values.Select(static value => value?.ToString() ?? string.Empty));
        }

        var timestamp = ReadTimestamp(payload, message.Timestamp);
        args = new ConsoleMessageEventArgs
        {
            Level = ReadConsoleLevel(ReadString(payload, "level")),
            Time = timestamp,
            Args = values,
            Frame = frame,
            Message = text ?? string.Empty,
        };

        return true;
    }

    internal static bool TryReadFrameDetached(BridgeMessage message, out string frameElementId)
    {
        frameElementId = string.Empty;

        if (!TryGetPayloadObject(message, BridgeEvent.FrameDetached, out var payload))
        {
            return false;
        }

        frameElementId = ReadString(payload, "frameElementId") ?? string.Empty;
        return !string.IsNullOrWhiteSpace(frameElementId);
    }

    internal static bool TryReadInterceptedRequest(BridgeMessage message, IFrame frame, out InterceptedRequestEventArgs args)
    {
        args = default!;

        if (!TryGetPayloadObject(message, BridgeEvent.RequestIntercepted, out var payload))
        {
            return false;
        }

        var url = ReadUri(payload, "url");
        if (url is null)
        {
            return false;
        }

        var body = ReadBody(payload);
        var request = new HttpsRequestMessage(new HttpMethod(ReadString(payload, "method") ?? HttpMethod.Get.Method), url);
        if (body.Length > 0)
        {
            request.Content = new ByteArrayContent(body);
        }

        ApplyHeaders(request, ReadHeaders(payload, "headers"));

        var isNavigate = ReadIsNavigate(payload);

        args = new InterceptedRequestEventArgs
        {
            IsNavigate = isNavigate,
            SupportsNavigationFulfillment = ReadOptionalBoolean(payload, "supportsNavigationFulfillment"),
            Request = request,
            Frame = frame,
        };

        return true;
    }

    internal static bool TryReadCallback(BridgeMessage message, out CallbackEventArgs args)
    {
        args = default!;

        if (!TryGetPayloadObject(message, BridgeEvent.Callback, out var payload))
        {
            return false;
        }

        var name = ReadString(payload, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        args = new CallbackEventArgs
        {
            Name = name,
            Args = ReadValues(payload, "args"),
            Code = ReadString(payload, "code"),
        };

        return true;
    }

    internal static bool TryReadCallbackFinalized(BridgeMessage message, out CallbackFinalizedEventArgs args)
    {
        args = default!;

        if (!TryGetPayloadObject(message, BridgeEvent.CallbackFinalized, out var payload))
        {
            return false;
        }

        var name = ReadString(payload, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        args = new CallbackFinalizedEventArgs
        {
            Name = name,
        };

        return true;
    }

    internal static bool TryReadInterceptedResponse(BridgeMessage message, IFrame frame, out InterceptedResponseEventArgs args)
    {
        args = default!;

        if (!TryGetPayloadObject(message, BridgeEvent.ResponseReceived, out var payload))
        {
            return false;
        }

        var response = new HttpsResponseMessage(ReadStatusCode(payload))
        {
            ReasonPhrase = ReadString(payload, "reasonPhrase"),
        };

        var body = ReadBody(payload);
        var content = ReadString(payload, "content");
        if (!string.IsNullOrWhiteSpace(content))
        {
            var stringContent = new StringContent(content, Encoding.UTF8);
            var contentType = ReadContentType(payload);
            stringContent.Headers.ContentType = !string.IsNullOrWhiteSpace(contentType)
                ? MediaTypeHeaderValue.Parse(contentType)
                : new MediaTypeHeaderValue("text/html");

            response.Content = stringContent;
        }
        else if (body.Length > 0)
        {
            response.Content = new ByteArrayContent(body);
        }

        ApplyHeaders(response, ReadHeaders(payload, "headers"));

        var url = ReadUri(payload, "url");
        if (url is not null)
        {
            response.RequestMessage = new HttpRequestMessage(new HttpMethod(ReadString(payload, "method") ?? HttpMethod.Get.Method), url);
        }

        args = new InterceptedResponseEventArgs
        {
            IsNavigate = ReadIsNavigate(payload),
            Response = response,
            Frame = frame,
        };

        return true;
    }

    private static bool TryGetPayloadObject(BridgeMessage message, BridgeEvent expectedEvent, out JsonElement payload)
    {
        payload = default;

        if (message.Type != BridgeMessageType.Event || message.Event != expectedEvent || message.Payload is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        payload = element;
        return true;
    }

    private static DateTimeOffset ReadTimestamp(JsonElement payload, long fallbackMilliseconds)
    {
        if (payload.TryGetProperty("ts", out var tsElement) && tsElement.TryGetInt64(out var timestamp))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
        }

        if (payload.TryGetProperty("timestamp", out var timestampElement) && timestampElement.TryGetInt64(out var milliseconds))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }

        return fallbackMilliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(fallbackMilliseconds)
            : DateTimeOffset.UtcNow;
    }

    private static ConsoleMessageLevel ReadConsoleLevel(string? level)
        => level?.Trim().ToLowerInvariant() switch
        {
            "warn" or "warning" => ConsoleMessageLevel.Warn,
            "error" => ConsoleMessageLevel.Error,
            "info" => ConsoleMessageLevel.Info,
            "debug" => ConsoleMessageLevel.Debug,
            _ => ConsoleMessageLevel.Log,
        };

    private static object?[] ReadValues(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return element.EnumerateArray().Select(ReadUntypedValue).ToArray();
    }

    private static object? ReadUntypedValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var int64Value) => int64Value,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.ToString(),
        };

    private static Uri? ReadUri(JsonElement payload, string propertyName)
        => payload.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String && Uri.TryCreate(element.GetString(), UriKind.Absolute, out var uri)
            ? uri
            : null;

    private static string? ReadString(JsonElement payload, string propertyName)
        => payload.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static bool ReadOptionalBoolean(JsonElement payload, string propertyName)
        => payload.TryGetProperty(propertyName, out var element)
            && element.ValueKind is JsonValueKind.True or JsonValueKind.False
            && element.GetBoolean();

    private static Dictionary<string, string>? ReadHeaders(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        Dictionary<string, string>? headers = null;
        foreach (var property in element.EnumerateObject())
        {
            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.ToString(),
                _ => null,
            };

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            headers ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            headers[property.Name] = value;
        }

        return headers;
    }

    private static byte[] ReadBody(JsonElement payload)
    {
        var value = ReadString(payload, "bodyBase64");
        return string.IsNullOrWhiteSpace(value)
            ? []
            : Convert.FromBase64String(value);
    }

    private static HttpStatusCode ReadStatusCode(JsonElement payload)
        => payload.TryGetProperty("statusCode", out var element) && element.TryGetInt32(out var statusCode)
            ? (HttpStatusCode)statusCode
            : HttpStatusCode.OK;

    private static bool ReadIsNavigate(JsonElement payload)
    {
        if (payload.TryGetProperty("isNavigate", out var element) && (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False))
        {
            return element.GetBoolean();
        }

        return string.Equals(ReadString(payload, "type"), "main_frame", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadContentType(JsonElement payload)
    {
        var headers = ReadHeaders(payload, "headers");
        if (headers is null)
        {
            return null;
        }

        return headers.TryGetValue("Content-Type", out var contentType) ? contentType : null;
    }

    private static void ApplyHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return;
        }

        foreach (var header in headers)
        {
            if (request.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                continue;
            }

            request.Content ??= new ByteArrayContent([]);
            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase) && request.Content.Headers.ContentType is not null)
            {
                continue;
            }

            request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private static void ApplyHeaders(HttpResponseMessage response, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return;
        }

        foreach (var header in headers)
        {
            if (response.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                continue;
            }

            response.Content ??= new ByteArrayContent([]);
            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase) && response.Content.Headers.ContentType is not null)
            {
                continue;
            }

            response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }
}