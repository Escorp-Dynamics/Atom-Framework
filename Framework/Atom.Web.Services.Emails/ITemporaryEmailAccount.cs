using Atom.Web.Emails.Services;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails;

/// <summary>
/// Представляет временный почтовый аккаунт.
/// </summary>
public interface ITemporaryEmailAccount : IDisposable, IMailAccount
{
    /// <summary>
    /// Доменная часть адреса после символа @.
    /// </summary>
    string Domain { get; }

    /// <summary>
    /// Внешний логгер аккаунта.
    /// </summary>
    ILogger? Logger { get; set; }

    /// <summary>
    /// Провайдер, создавший аккаунт.
    /// </summary>
    ITemporaryEmailProvider? Provider { get; set; }

}