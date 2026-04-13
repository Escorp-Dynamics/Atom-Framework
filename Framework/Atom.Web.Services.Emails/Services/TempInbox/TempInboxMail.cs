namespace Atom.Web.Emails.Services;

/// <summary>
/// Письмо TempInbox, загруженное из публичного inbox API.
/// </summary>
public sealed class TempInboxMail : Mail
{
    internal TempInboxMail(string upstreamId, Guid id, string from, string to, string subject, string body)
        : base(id, from, to, subject, body)
    {
        UpstreamId = upstreamId;
    }

    /// <summary>
    /// Идентификатор письма в upstream inbox API.
    /// </summary>
    public string UpstreamId { get; }
}