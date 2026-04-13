namespace Atom.Web.Emails.Services;

/// <summary>
/// Письмо Mohmal, распарсенное из HTML inbox.
/// </summary>
public sealed class MohmalMail : Mail
{
    internal MohmalMail(string upstreamId, Guid id, string from, string to, string subject, string body)
        : base(id, from, to, subject, body)
    {
        UpstreamId = upstreamId;
    }

    /// <summary>
    /// Идентификатор письма внутри HTML inbox.
    /// </summary>
    public string UpstreamId { get; }
}