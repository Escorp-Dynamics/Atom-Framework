using System.Text.Json.Serialization;

namespace Atom.Net.Browsing.WebDriver;

/// <summary>
/// Действие, применяемое к перехваченному запросу.
/// </summary>
public enum InterceptAction
{
    /// <summary>
    /// Продолжить запрос (с возможными модификациями).
    /// </summary>
    Continue,

    /// <summary>
    /// Отменить запрос.
    /// </summary>
    Abort,

    /// <summary>
    /// Вернуть кастомный ответ вместо реального запроса.
    /// </summary>
    Fulfill,
}

/// <summary>
/// Параметры продолжения перехваченного запроса.
/// Позволяет изменить URL (redirect) и заголовки перед отправкой.
/// </summary>
public sealed class InterceptedRequestContinuation
{
    /// <summary>
    /// Новый URL для редиректа. <see langword="null"/> — продолжить с оригинальным URL.
    /// </summary>
#pragma warning disable CA1056 // Wire-протокол с JS — URL передаётся как строка.
    public string? Url { get; init; }
#pragma warning restore CA1056

    /// <summary>
    /// Заголовки запроса для замены. <see langword="null"/> — без изменений.
    /// </summary>
    public IDictionary<string, string>? Headers { get; init; }
}

/// <summary>
/// Параметры кастомного ответа для перехваченного запроса.
/// </summary>
public sealed class InterceptedRequestFulfillment
{
    /// <summary>
    /// HTTP-код ответа.
    /// </summary>
    public int StatusCode { get; init; } = 200;

    /// <summary>
    /// MIME-тип ответа.
    /// </summary>
    public string ContentType { get; init; } = "text/html";

    /// <summary>
    /// Заголовки ответа.
    /// </summary>
    public IDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Тело ответа.
    /// </summary>
    public string? Body { get; init; }
}

/// <summary>
/// Аргументы события перехвата сетевого запроса.
/// </summary>
/// <remarks>
/// Обработчик ДОЛЖЕН вызвать один из методов: <see cref="Continue()"/>,
/// <see cref="Abort"/> или <see cref="Fulfill"/> — иначе запрос зависнет.
/// </remarks>
public sealed class InterceptedRequestEventArgs : EventArgs
{
    private readonly TaskCompletionSource<InterceptDecision> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Идентификатор перехваченного запроса.
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// URL запроса.
    /// </summary>
#pragma warning disable CA1056 // Wire-протокол с JS — URL передаётся как строка.
    public required string Url { get; init; }
#pragma warning restore CA1056

    /// <summary>
    /// HTTP-метод запроса.
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// Тип ресурса (<c>main_frame</c>, <c>script</c>, <c>image</c> и т.д.).
    /// </summary>
    public required string ResourceType { get; init; }

    /// <summary>
    /// Идентификатор вкладки, инициировавшей запрос.
    /// </summary>
    public required string TabId { get; init; }

    /// <summary>
    /// Продолжить запрос без изменений.
    /// </summary>
    public void Continue() =>
        decision.TrySetResult(new InterceptDecision { Action = InterceptAction.Continue });

    /// <summary>
    /// Продолжить запрос с модификациями (URL-redirect и/или заголовки).
    /// </summary>
    /// <param name="continuation">Параметры модификации.</param>
    public void Continue(InterceptedRequestContinuation continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        decision.TrySetResult(new InterceptDecision { Action = InterceptAction.Continue, Continuation = continuation });
    }

    /// <summary>
    /// Отменить запрос.
    /// </summary>
    public void Abort() =>
        decision.TrySetResult(new InterceptDecision { Action = InterceptAction.Abort });

    /// <summary>
    /// Ответить кастомным телом вместо реального запроса.
    /// </summary>
    /// <param name="fulfillment">Параметры ответа.</param>
    public void Fulfill(InterceptedRequestFulfillment fulfillment)
    {
        ArgumentNullException.ThrowIfNull(fulfillment);
        decision.TrySetResult(new InterceptDecision { Action = InterceptAction.Fulfill, Fulfillment = fulfillment });
    }

    /// <summary>
    /// Ожидает решения обработчика. Используется BridgeServer.
    /// </summary>
    internal Task<InterceptDecision> WaitForDecisionAsync(CancellationToken cancellationToken) =>
        decision.Task.WaitAsync(cancellationToken);

    /// <summary>
    /// Устанавливает решение по умолчанию (continue), если обработчик не принял решение.
    /// Вызывается автоматически, когда обработчик не вызвал <see cref="Continue()"/>, <see cref="Abort"/> или <see cref="Fulfill"/>.
    /// </summary>
    internal void SetDefaultIfPending() =>
        decision.TrySetResult(new InterceptDecision { Action = InterceptAction.Continue });
}

/// <summary>
/// Решение, принятое обработчиком для перехваченного запроса.
/// </summary>
internal sealed class InterceptDecision
{
    public required InterceptAction Action { get; init; }
    public InterceptedRequestContinuation? Continuation { get; init; }
    public InterceptedRequestFulfillment? Fulfillment { get; init; }
}

/// <summary>
/// Контекст JSON-сериализации для данных интерцепта.
/// </summary>
[JsonSerializable(typeof(InterceptHttpRequest))]
[JsonSerializable(typeof(InterceptHttpResponse))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class InterceptJsonContext : JsonSerializerContext;

/// <summary>
/// Данные запроса, полученные от background.js через sync XHR.
/// </summary>
internal sealed class InterceptHttpRequest
{
    public string? RequestId { get; set; }
    public string? Url { get; set; }
    public string? Method { get; set; }
    public string? Type { get; set; }
    public string? TabId { get; set; }
}

/// <summary>
/// Ответ на sync XHR от background.js — решение C# обработчика.
/// </summary>
internal sealed class InterceptHttpResponse
{
    /// <summary>
    /// Значение action: continue, abort или fulfill.
    /// </summary>
    public required string Action { get; set; }

    /// <summary>
    /// URL для редиректа (при continue с URL или при fulfill через bridge).
    /// </summary>
#pragma warning disable CA1056 // Wire-протокол с JS — URL передаётся как строка.
    public string? Url { get; set; }
#pragma warning restore CA1056

    /// <summary>
    /// Заголовки для модификации (при continue с headers).
    /// </summary>
    public IDictionary<string, string>? Headers { get; set; }
}
