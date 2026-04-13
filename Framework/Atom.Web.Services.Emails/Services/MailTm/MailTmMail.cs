namespace Atom.Web.Emails.Services;

/// <summary>
/// Письмо mail.tm с поддержкой upstream-операций mark-as-read и delete.
/// </summary>
public sealed class MailTmMail : HttpTemporaryEmailMail<MailTmAccount>
{
    internal MailTmMail(
        MailTmAccount account,
        string upstreamId,
        Guid id,
        string from,
        string to,
        string subject,
        string body,
        bool isRead)
        : base(account, upstreamId, id, from, to, subject, body, isRead) { }
}