namespace Atom.Web.Emails.Services;

/// <summary>
/// Письмо Moakt, загруженное из HTML inbox.
/// </summary>
public sealed class MoaktMail : Mail
{
    internal MoaktMail(string upstreamId, Guid id, string from, string to, string subject, string body)
        : base(id, from, to, subject, body)
    {
        UpstreamId = upstreamId;
    }

    /// <summary>
    /// Идентификатор письма в upstream inbox.
    /// </summary>
    public string UpstreamId { get; }
}