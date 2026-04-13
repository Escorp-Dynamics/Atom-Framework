using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atom.Web.Services.Polymarket;

/// <summary>
/// Тип webhook-платформы (определяет формат payload).
/// </summary>
public enum PolymarketWebhookType : byte
{
    /// <summary>Произвольный JSON POST.</summary>
    Generic,

    /// <summary>Telegram Bot API (sendMessage).</summary>
    Telegram,

    /// <summary>Discord Webhook (embeds).</summary>
    Discord,

    /// <summary>Slack Incoming Webhook (blocks).</summary>
    Slack
}

/// <summary>
/// Конфигурация webhook-эндпоинта.
/// </summary>
public sealed class PolymarketWebhookConfig
{
    /// <summary>Уникальный идентификатор.</summary>
    public required string Id { get; init; }

    /// <summary>URL для отправки (HTTPS).</summary>
    public required string Url { get; init; }

    /// <summary>Тип платформы.</summary>
    public PolymarketWebhookType Type { get; init; }

    /// <summary>Telegram-специфичные: chat_id.</summary>
    public string? TelegramChatId { get; init; }

    /// <summary>Активен ли webhook.</summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Аргументы события отправки webhook.
/// </summary>
public sealed class PolymarketWebhookSentEventArgs(
    PolymarketWebhookConfig config,
    bool success,
    int statusCode) : EventArgs
{
    /// <summary>Конфигурация webhook.</summary>
    public PolymarketWebhookConfig Config { get; } = config;

    /// <summary>Успешность отправки.</summary>
    public bool Success { get; } = success;

    /// <summary>HTTP статус-код ответа.</summary>
    public int StatusCode { get; } = statusCode;

    /// <summary>Ошибка, если отправка не удалась.</summary>
    public Exception? Error { get; init; }
}

/// <summary>
/// Отправляет алерты и сигналы через HTTP webhooks в Telegram, Discord, Slack и произвольные URL.
/// </summary>
/// <remarks>
/// Совместим с NativeAOT — использует source-generated JSON.
/// Потокобезопасен. Все отправки — асинхронные, неблокирующие.
/// </remarks>
public sealed class PolymarketWebhookNotifier : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly Dictionary<string, PolymarketWebhookConfig> webhooks = [];
    private readonly object syncRoot = new();
    private bool isDisposed;

    /// <summary>
    /// Событие при успешной/неудачной отправке webhook.
    /// </summary>
    public event AsyncEventHandler<PolymarketWebhookNotifier, PolymarketWebhookSentEventArgs>? WebhookSent;

    /// <summary>
    /// Инициализирует нотификатор с собственным HttpClient.
    /// </summary>
    public PolymarketWebhookNotifier()
    {
        httpClient = new HttpClient();
        ownsHttpClient = true;
    }

    /// <summary>
    /// Инициализирует нотификатор с внешним HttpClient.
    /// </summary>
    public PolymarketWebhookNotifier(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this.httpClient = httpClient;
        ownsHttpClient = false;
    }

    /// <summary>
    /// Добавляет webhook-конфигурацию.
    /// </summary>
    public void AddWebhook(PolymarketWebhookConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ValidateUrl(config.Url);
        lock (syncRoot) webhooks[config.Id] = config;
    }

    /// <summary>
    /// Удаляет webhook.
    /// </summary>
    public void RemoveWebhook(string id)
    {
        lock (syncRoot) webhooks.Remove(id);
    }

    /// <summary>
    /// Возвращает все зарегистрированные webhooks.
    /// </summary>
    public PolymarketWebhookConfig[] GetWebhooks()
    {
        lock (syncRoot) return [.. webhooks.Values];
    }

    /// <summary>
    /// Подключается к <see cref="PolymarketAlertSystem"/> для автоматической отправки алертов.
    /// </summary>
    public void ConnectAlertSystem(PolymarketAlertSystem alertSystem)
    {
        ArgumentNullException.ThrowIfNull(alertSystem);
        alertSystem.AlertTriggered += OnAlertTriggeredAsync;
    }

    /// <summary>
    /// Отключается от <see cref="PolymarketAlertSystem"/>.
    /// </summary>
    public void DisconnectAlertSystem(PolymarketAlertSystem alertSystem)
    {
        ArgumentNullException.ThrowIfNull(alertSystem);
        alertSystem.AlertTriggered -= OnAlertTriggeredAsync;
    }

    /// <summary>
    /// Подключается к <see cref="PolymarketOrderExecutor"/> для уведомлений об ордерах.
    /// </summary>
    public void ConnectOrderExecutor(PolymarketOrderExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        executor.OrderExecuted += OnOrderExecutedAsync;
    }

    /// <summary>
    /// Отключается от <see cref="PolymarketOrderExecutor"/>.
    /// </summary>
    public void DisconnectOrderExecutor(PolymarketOrderExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        executor.OrderExecuted -= OnOrderExecutedAsync;
    }

    /// <summary>
    /// Отправляет произвольное текстовое сообщение во все активные webhooks.
    /// </summary>
    public async ValueTask SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);

        PolymarketWebhookConfig[] configs;
        lock (syncRoot) configs = [.. webhooks.Values];

        foreach (var config in configs)
        {
            if (!config.IsEnabled) continue;
            await SendToWebhookAsync(config, message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Отправляет сообщение в конкретный webhook по ID.
    /// </summary>
    public async ValueTask SendMessageAsync(string webhookId, string message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);

        PolymarketWebhookConfig? config;
        lock (syncRoot) webhooks.TryGetValue(webhookId, out config);

        if (config is null || !config.IsEnabled) return;

        await SendToWebhookAsync(config, message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Обработчик алертов.
    /// </summary>
    private async ValueTask OnAlertTriggeredAsync(PolymarketAlertSystem sender, PolymarketAlertTriggeredEventArgs e)
    {
        var message = FormatAlertMessage(e);
        await SendMessageAsync(message).ConfigureAwait(false);
    }

    /// <summary>
    /// Обработчик исполнения ордеров.
    /// </summary>
    private async ValueTask OnOrderExecutedAsync(PolymarketOrderExecutor sender, PolymarketOrderExecutedEventArgs e)
    {
        var message = FormatOrderMessage(e);
        await SendMessageAsync(message).ConfigureAwait(false);
    }

    /// <summary>
    /// Отправляет сообщение в конкретный webhook с форматированием под платформу.
    /// </summary>
    private async ValueTask SendToWebhookAsync(
        PolymarketWebhookConfig config,
        string message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = FormatPayload(config, message);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync(config.Url, content, cancellationToken).ConfigureAwait(false);

            if (WebhookSent is not null)
            {
                await WebhookSent.Invoke(this, new PolymarketWebhookSentEventArgs(
                    config, response.IsSuccessStatusCode, (int)response.StatusCode)).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            if (WebhookSent is not null)
            {
                await WebhookSent.Invoke(this, new PolymarketWebhookSentEventArgs(
                    config, false, 0)
                { Error = ex }).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Форматирует payload для конкретной платформы.
    /// </summary>
    private static string FormatPayload(PolymarketWebhookConfig config, string message)
    {
        return config.Type switch
        {
            PolymarketWebhookType.Telegram => FormatTelegramPayload(config, message),
            PolymarketWebhookType.Discord => FormatDiscordPayload(message),
            PolymarketWebhookType.Slack => FormatSlackPayload(message),
            _ => FormatGenericPayload(message)
        };
    }

    /// <summary>
    /// Telegram: sendMessage FormData JSON.
    /// </summary>
    private static string FormatTelegramPayload(PolymarketWebhookConfig config, string message)
    {
        var payload = new TelegramPayload
        {
            ChatId = config.TelegramChatId ?? "",
            Text = message,
            ParseMode = "HTML"
        };
        return JsonSerializer.Serialize(payload, PolymarketWebhookJsonContext.Default.TelegramPayload);
    }

    /// <summary>
    /// Discord: webhook embeds.
    /// </summary>
    private static string FormatDiscordPayload(string message)
    {
        var payload = new DiscordPayload
        {
            Content = message,
            Username = "Polymarket Bot"
        };
        return JsonSerializer.Serialize(payload, PolymarketWebhookJsonContext.Default.DiscordPayload);
    }

    /// <summary>
    /// Slack: incoming webhook.
    /// </summary>
    private static string FormatSlackPayload(string message)
    {
        var payload = new SlackPayload { Text = message };
        return JsonSerializer.Serialize(payload, PolymarketWebhookJsonContext.Default.SlackPayload);
    }

    /// <summary>
    /// Generic: произвольный JSON.
    /// </summary>
    private static string FormatGenericPayload(string message)
    {
        var payload = new GenericPayload
        {
            Message = message,
            Source = "Polymarket",
            Timestamp = DateTimeOffset.UtcNow
        };
        return JsonSerializer.Serialize(payload, PolymarketWebhookJsonContext.Default.GenericPayload);
    }

    /// <summary>
    /// Форматирует алерт в читаемое сообщение.
    /// </summary>
    private static string FormatAlertMessage(PolymarketAlertTriggeredEventArgs e)
    {
        using var sb = new Atom.Text.ValueStringBuilder();
        sb.Append("⚠ Алерт Polymarket: ");
        sb.Append(e.Alert.Description ?? e.Alert.Id);
        sb.Append(" | Условие: ").Append(e.Alert.Condition);
        sb.Append(' ').Append(e.Alert.Direction);
        sb.Append(' ').Append(e.Alert.Threshold.ToString("G", CultureInfo.InvariantCulture));
        sb.Append(" | Текущее значение: ").Append(e.CurrentValue.ToString("F4", CultureInfo.InvariantCulture));
        sb.Append(" | ").Append(e.TriggeredAt.ToString("u", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>
    /// Форматирует ордер в читаемое сообщение.
    /// </summary>
    private static string FormatOrderMessage(PolymarketOrderExecutedEventArgs e)
    {
        using var sb = new Atom.Text.ValueStringBuilder();
        sb.Append(e.Success ? "✅ Ордер исполнен" : "❌ Ордер не исполнен");
        sb.Append(" | ").Append(e.Signal.Action);
        sb.Append(' ').Append(e.Signal.AssetId);
        sb.Append(" qty=").Append(e.Signal.Quantity.ToString("G", CultureInfo.InvariantCulture));
        if (e.Signal.Price is not null)
            sb.Append(" @ ").Append(e.Signal.Price);
        if (e.Response?.OrderId is not null)
            sb.Append(" | OrderId: ").Append(e.Response.OrderId);
        if (e.Error is not null)
            sb.Append(" | Ошибка: ").Append(e.Error.Message);
        sb.Append(" | ").Append(e.ExecutedAt.ToString("u", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>
    /// Валидирует URL webhook (должен быть HTTPS).
    /// </summary>
    private static void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Невалидный URL: {url}", nameof(url));
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Webhook URL должен использовать HTTPS", nameof(url));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        if (ownsHttpClient)
            httpClient.Dispose();

        GC.SuppressFinalize(this);
    }
}

#region Webhook Payloads

/// <summary>Telegram sendMessage payload.</summary>
internal sealed class TelegramPayload
{
    [JsonPropertyName("chat_id")]
    public required string ChatId { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("parse_mode")]
    public string? ParseMode { get; init; }
}

/// <summary>Discord webhook payload.</summary>
internal sealed class DiscordPayload
{
    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("username")]
    public string? Username { get; init; }
}

/// <summary>Slack incoming webhook payload.</summary>
internal sealed class SlackPayload
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

/// <summary>Generic webhook payload.</summary>
internal sealed class GenericPayload
{
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }
}

#endregion

/// <summary>
/// Source-generated JSON для webhook payloads (NativeAOT-совместимо).
/// </summary>
[JsonSerializable(typeof(TelegramPayload))]
[JsonSerializable(typeof(DiscordPayload))]
[JsonSerializable(typeof(SlackPayload))]
[JsonSerializable(typeof(GenericPayload))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class PolymarketWebhookJsonContext : JsonSerializerContext;
