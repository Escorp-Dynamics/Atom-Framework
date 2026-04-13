namespace Atom.Web.Emails.Services;

/// <summary>
/// Письмо temp-mail.org с поддержкой upstream-удаления.
/// </summary>
public sealed class TempMailOrgMail : Mail
{
    private readonly TempMailOrgAccount account;

    internal TempMailOrgMail(
        TempMailOrgAccount account,
        string upstreamId,
        Guid id,
        string from,
        string to,
        string subject,
        string body)
        : base(id, from, to, subject, body)
    {
        this.account = account;
        UpstreamId = upstreamId;
    }

    /// <summary>
    /// Идентификатор письма в upstream API.
    /// </summary>
    public string UpstreamId { get; }

    /// <inheritdoc/>
    public override ValueTask DeleteAsync(CancellationToken cancellationToken)
        => account.DeleteMailAsync(UpstreamId, cancellationToken);
}