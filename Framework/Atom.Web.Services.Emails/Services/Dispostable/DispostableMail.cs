namespace Atom.Web.Emails.Services;

/// <summary>
/// Письмо Dispostable с поддержкой upstream-удаления.
/// </summary>
public sealed class DispostableMail : Mail
{
    private readonly DispostableAccount account;

    internal DispostableMail(
        DispostableAccount account,
        string upstreamId,
        Guid id,
        string from,
        string to,
        string subject,
        string body)
        : base(id, from, to, subject, body)
    {
        this.account = account;
        UpstreamId = upstreamId;
    }

    /// <summary>
    /// Идентификатор письма в upstream feed.
    /// </summary>
    public string UpstreamId { get; }

    /// <inheritdoc/>
    public override ValueTask DeleteAsync(CancellationToken cancellationToken)
        => account.DeleteMailAsync(UpstreamId, cancellationToken);
}