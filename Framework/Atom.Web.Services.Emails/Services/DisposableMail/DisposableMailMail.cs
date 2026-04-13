namespace Atom.Web.Emails.Services;

/// <summary>
/// Письмо DisposableMail, загруженное из HTML inbox.
/// </summary>
public sealed class DisposableMailMail : Mail
{
    internal DisposableMailMail(string upstreamId, Guid id, string from, string to, string subject, string body)
        : base(id, from, to, subject, body)
    {
        UpstreamId = upstreamId;
    }

    /// <summary>
    /// Идентификатор письма в upstream inbox.
    /// </summary>
    public string UpstreamId { get; }
}