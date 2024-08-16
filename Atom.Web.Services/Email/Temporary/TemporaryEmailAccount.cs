using Atom.Web.Emails;

namespace Atom.Web.Services.Email.Temporary;

/// <summary>
/// Представляет временный аккаунт электронной почты.
/// </summary>
public class TemporaryEmailAccount : EmailAccount
{
    private readonly ITemporaryEmailService service;

    internal TemporaryEmailAccount(ITemporaryEmailService service, string login, string password) : base(login, password) => this.service = service;
}