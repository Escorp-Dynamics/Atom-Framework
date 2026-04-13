namespace Atom.Web.Emails.Services;

/// <summary>
/// Письмо generator.email, загруженное из HTML inbox.
/// </summary>
public sealed class GeneratorEmailMail : Mail
{
    internal GeneratorEmailMail(string upstreamId, Guid id, string from, string to, string subject, string body)
        : base(id, from, to, subject, body)
    {
        UpstreamId = upstreamId;
    }

    /// <summary>
    /// Идентификатор письма в upstream inbox.
    /// </summary>
    public string UpstreamId { get; }
}