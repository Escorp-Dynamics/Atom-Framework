namespace Atom.Web.Emails.Services;

/// <summary>
/// Общее письмо HTTP-ориентированного временного почтового провайдера.
/// </summary>
public abstract class HttpTemporaryEmailMail<TAccount> : Mail
    where TAccount : class, IHttpTemporaryEmailMailOperations
{
    private readonly TAccount account;

    /// <summary>
    /// Инициализирует письмо, связанное с upstream HTTP-аккаунтом.
    /// </summary>
    protected HttpTemporaryEmailMail(
        TAccount account,
        string upstreamId,
        Guid id,
        string from,
        string to,
        string subject,
        string body,
        bool isRead)
        : base(id, from, to, subject, body)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(upstreamId);

        this.account = account;
        UpstreamId = upstreamId;
        IsRead = isRead;
    }

    /// <summary>
    /// Идентификатор письма в upstream API.
    /// </summary>
    public string UpstreamId { get; }

    /// <inheritdoc/>
    public override async ValueTask MarkAsReadAsync(CancellationToken cancellationToken)
    {
        await account.MarkUpstreamMailAsReadAsync(UpstreamId, cancellationToken).ConfigureAwait(false);
        await base.MarkAsReadAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override ValueTask DeleteAsync(CancellationToken cancellationToken)
        => account.DeleteUpstreamMailAsync(UpstreamId, cancellationToken);
}