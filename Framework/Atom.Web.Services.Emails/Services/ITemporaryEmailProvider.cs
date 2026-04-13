using Atom.Web.Services;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails.Services;

/// <summary>
/// Представляет контракт провайдера временной почты.
/// </summary>
public interface ITemporaryEmailProvider : IWebService
{
    /// <summary>
    /// Внешний логгер провайдера.
    /// </summary>
    ILogger? Logger { get; set; }

    /// <summary>
    /// Домены, которые провайдер умеет выдавать. Пустой набор означает отсутствие декларативных ограничений.
    /// </summary>
    IEnumerable<string> AvailableDomains { get; }

    /// <summary>
    /// Проверяет, подходит ли провайдер для создания аккаунта по запросу.
    /// </summary>
    bool CanCreate(TemporaryEmailAccountCreateSettings request);

    /// <summary>
    /// Создаёт новый временный почтовый аккаунт.
    /// </summary>
    ValueTask<ITemporaryEmailAccount> CreateAccountAsync(TemporaryEmailAccountCreateSettings request, CancellationToken cancellationToken);

    /// <summary>
    /// Создаёт новый временный почтовый аккаунт.
    /// </summary>
    ValueTask<ITemporaryEmailAccount> CreateAccountAsync(CancellationToken cancellationToken)
        => CreateAccountAsync(TemporaryEmailAccountCreateSettings.Empty, cancellationToken);
}