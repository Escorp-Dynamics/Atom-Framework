namespace Atom.Web.Emails.Services;

/// <summary>
/// Письмо DropMail, загруженное через GraphQL session query.
/// </summary>
public sealed class DropMailMail : Mail
{
    internal DropMailMail(
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
    /// Идентификатор письма в upstream API.
    /// </summary>
    public string UpstreamId { get; }
}