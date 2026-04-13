namespace Atom.Web.Emails.Services;

/// <summary>
/// Письмо Maildrop, загруженное из публичного inbox API.
/// </summary>
public sealed class MaildropMail : Mail
{
    internal MaildropMail(string upstreamId, Guid id, string from, string to, string subject, string body)
        : base(id, from, to, subject, body)
    {
        UpstreamId = upstreamId;
    }

    /// <summary>
    /// Идентификатор письма в upstream inbox API.
    /// </summary>
    public string UpstreamId { get; }
}