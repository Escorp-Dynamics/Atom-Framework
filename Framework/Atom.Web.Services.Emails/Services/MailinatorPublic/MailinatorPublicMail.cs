namespace Atom.Web.Emails.Services;

/// <summary>
/// Письмо публичного inbox Mailinator.
/// </summary>
public sealed class MailinatorPublicMail : Mail
{
    internal MailinatorPublicMail(string upstreamId, Guid id, string from, string to, string subject, string body)
        : base(id, from, to, subject, body)
    {
        UpstreamId = upstreamId;
    }

    /// <summary>
    /// Идентификатор письма в публичном inbox API.
    /// </summary>
    public string UpstreamId { get; }
}