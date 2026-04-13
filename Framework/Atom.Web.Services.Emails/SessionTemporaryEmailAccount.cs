using Atom.Web.Emails.Services;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails;

/// <summary>
/// Общая базовая реализация временного почтового аккаунта, привязанного к upstream session key.
/// </summary>
public abstract class SessionTemporaryEmailAccount<TProvider> : TemporaryEmailAccount
    where TProvider : class, ITemporaryEmailProvider
{
    private readonly string unsupportedSendMessage;

    /// <summary>
    /// Инициализирует session-oriented аккаунт.
    /// </summary>
    protected SessionTemporaryEmailAccount(
        TProvider provider,
        Guid id,
        string address,
        string sessionKey,
        string unsupportedSendMessage,
        ILogger? logger = null)
        : base(id, address, string.Empty, logger)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(unsupportedSendMessage);

        ProviderCore = provider;
        Provider = provider;
        SessionKey = sessionKey;
        this.unsupportedSendMessage = unsupportedSendMessage;
    }

    /// <summary>
    /// Конкретный провайдер текущего аккаунта.
    /// </summary>
    protected TProvider ProviderCore { get; }

    /// <summary>
    /// Session key, возвращенный upstream API.
    /// </summary>
    protected string SessionKey { get; }

    /// <inheritdoc/>
    public override ValueTask ConnectAsync(CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public override ValueTask DisconnectAsync(CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public override ValueTask SendAsync(IMail mail, CancellationToken cancellationToken)
        => ValueTask.FromException(new NotSupportedException(unsupportedSendMessage));
}