using Atom.Web.Emails.Services;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails;

/// <summary>
/// Общая базовая реализация временного почтового аккаунта, связанного с конкретным провайдером без отдельной upstream session key.
/// </summary>
public abstract class ProviderTemporaryEmailAccount<TProvider> : TemporaryEmailAccount
    where TProvider : class, ITemporaryEmailProvider
{
    private readonly string unsupportedSendMessage;

    /// <summary>
    /// Инициализирует provider-bound аккаунт.
    /// </summary>
    protected ProviderTemporaryEmailAccount(
        TProvider provider,
        Guid id,
        string address,
        string unsupportedSendMessage,
        ILogger? logger = null)
        : base(id, address, string.Empty, logger)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(unsupportedSendMessage);

        ProviderCore = provider;
        Provider = provider;
        this.unsupportedSendMessage = unsupportedSendMessage;
    }

    /// <summary>
    /// Конкретный провайдер текущего аккаунта.
    /// </summary>
    protected TProvider ProviderCore { get; }

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