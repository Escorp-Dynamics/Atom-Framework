namespace Atom.Web.Emails.Services;

/// <summary>
/// Письмо mail.gw с поддержкой upstream-операций mark-as-read и delete.
/// </summary>
public sealed class MailGwMail : HttpTemporaryEmailMail<MailGwAccount>
{
    internal MailGwMail(
        MailGwAccount account,
        string upstreamId,
        Guid id,
        string from,
        string to,
        string subject,
        string body,
        bool isRead)
        : base(account, upstreamId, id, from, to, subject, body, isRead) { }
}