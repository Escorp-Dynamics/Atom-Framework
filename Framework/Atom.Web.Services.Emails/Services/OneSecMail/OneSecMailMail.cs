namespace Atom.Web.Emails.Services;

/// <summary>
/// Письмо 1SecMail с поддержкой upstream-удаления.
/// </summary>
public sealed class OneSecMailMail : Mail
{
    private readonly OneSecMailAccount account;

    internal OneSecMailMail(
        OneSecMailAccount account,
        string upstreamId,
        Guid id,
        string from,
        string to,
        string subject,
        string body,
        bool isRead)
        : base(id, from, to, subject, body)
    {
        this.account = account;
        UpstreamId = upstreamId;
        IsRead = isRead;
    }

    /// <summary>
    /// Идентификатор письма в upstream API.
    /// </summary>
    public string UpstreamId { get; }

    /// <inheritdoc/>
    public override ValueTask DeleteAsync(CancellationToken cancellationToken)
        => account.DeleteMailAsync(UpstreamId, cancellationToken);
}