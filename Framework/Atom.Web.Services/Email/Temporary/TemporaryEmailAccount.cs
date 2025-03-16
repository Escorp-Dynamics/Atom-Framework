/*using Atom.Web.Emails;

namespace Atom.Web.Services.Email.Temporary;

/// <summary>
/// Представляет временный аккаунт электронной почты.
/// </summary>
public class TemporaryEmailAccount : EmailAccount
{
#pragma warning disable IDE0052 // Удалить непрочитанные закрытые члены
    private readonly ITemporaryEmailService service;
#pragma warning restore IDE0052 // Удалить непрочитанные закрытые члены

    internal TemporaryEmailAccount(ITemporaryEmailService service, string login, string password) : base(login, password) => this.service = service;
}*/