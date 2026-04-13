namespace Atom.Web.Emails.Services;

/// <summary>
/// Письмо Mailnesia, загруженное из XML mailbox feed.
/// </summary>
public sealed class MailnesiaMail : Mail
{
    internal MailnesiaMail(
        string upstreamId,
        Guid id,
        string from,
        string to,
        string subject,
        string body,
        bool isRead)
        : base(id, from, to, subject, body)
    {
        UpstreamId = upstreamId;
        IsRead = isRead;
    }

    /// <summary>
    /// Идентификатор письма в upstream XML feed.
    /// </summary>
    public string UpstreamId { get; }
}