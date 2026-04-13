using System.Collections.Generic;
using System.Threading;
using Atom.Web.Emails.Services;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails;

/// <summary>
/// Представляет базовую реализацию временного почтового аккаунта.
/// </summary>
public abstract class TemporaryEmailAccount : MailAccount, ITemporaryEmailAccount
{
    private readonly SemaphoreSlim refreshGate = new(initialCount: 1, maxCount: 1);
    private HashSet<Guid> knownMessageIds = [];
    private Mail[] inbox = [];
    private int disposeState;

    /// <summary>
    /// Инициализирует новый временный почтовый аккаунт.
    /// </summary>
    protected TemporaryEmailAccount(Guid id, string address, string password = "", ILogger? logger = null)
        : base(
            id,
            TemporaryEmailAddressUtility.ExtractUserName(address),
            password,
            address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        Logger = logger;

        Domain = TemporaryEmailAddressUtility.ExtractDomain(address);
    }

    /// <inheritdoc/>
    public string Domain { get; }

    /// <inheritdoc/>
    public ILogger? Logger { get; set; }

    /// <inheritdoc/>
    public ITemporaryEmailProvider? Provider { get; set; }

    /// <inheritdoc/>
    public override IEnumerable<Mail> Inbox => Volatile.Read(ref inbox);

    /// <inheritdoc/>
    public override int Count => Volatile.Read(ref inbox).Length;

    /// <inheritdoc/>
    public override event MutableEventHandler<IMailAccount, MailReceivedEventArgs>? MailReceived;

    /// <inheritdoc/>
    public override async ValueTask<IEnumerable<Mail>> RefreshInboxAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        Mail[] snapshot;
        Mail[] newlyDiscovered;

        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loadedMessages = await LoadMessagesAsync(cancellationToken).ConfigureAwait(false);
            var nextMessages = new List<Mail>();
            var nextKnownMessageIds = new HashSet<Guid>();
            var discoveredMessages = new List<Mail>();

            foreach (var email in loadedMessages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ArgumentNullException.ThrowIfNull(email);

                if (email.Id == Guid.Empty)
                {
                    throw new InvalidOperationException("Провайдер вернул письмо без идентификатора.");
                }

                if (!nextKnownMessageIds.Add(email.Id))
                {
                    continue;
                }

                nextMessages.Add(email);
                if (!knownMessageIds.Contains(email.Id))
                {
                    discoveredMessages.Add(email);
                }
            }

            knownMessageIds = nextKnownMessageIds;
            snapshot = [.. nextMessages];
            Volatile.Write(ref inbox, snapshot);
            newlyDiscovered = [.. discoveredMessages];
        }
        finally
        {
            refreshGate.Release();
        }

        var handler = MailReceived;
        if (handler is not null)
        {
            foreach (var email in newlyDiscovered)
            {
                handler(this, new MailReceivedEventArgs(email));
            }
        }

        return snapshot;
    }

    /// <summary>
    /// Загружает актуальный snapshot inbox из upstream-провайдера.
    /// </summary>
    protected abstract ValueTask<IEnumerable<Mail>> LoadMessagesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Высвобождает управляемые ресурсы аккаунта.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref disposeState, value: 1) != 0)
        {
            return;
        }

        if (disposing)
        {
            refreshGate.Dispose();
        }
    }

    /// <summary>
    /// Высвобождает ресурсы аккаунта.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Проверяет, что аккаунт не был освобождён.
    /// </summary>
    protected void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposeState) != 0, GetType().Name);
}