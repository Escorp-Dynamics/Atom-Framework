using Atom.Architect.Components;
using Atom.Architect.Factories;
using Atom.Web.Emails.Services;
using Microsoft.Extensions.Logging;

namespace Atom.Web.Emails;

/// <summary>
/// Представляет базовый интерфейс для реализации фабрики временных почтовых аккаунтов.
/// </summary>
public interface ITemporaryEmailFactory<TTemporaryEmailProvider, out TTemporaryEmailFactory> :
    IComponentOwner<TTemporaryEmailFactory>,
    IAsyncFactory<ITemporaryEmailAccount>,
    IDisposable
    where TTemporaryEmailProvider : ITemporaryEmailProvider
    where TTemporaryEmailFactory : ITemporaryEmailFactory<TTemporaryEmailProvider, TTemporaryEmailFactory>
{
    /// <summary>
    /// Логгер фабрики.
    /// </summary>
    ILogger? Logger { get; set; }

    /// <summary>
    /// Текущее число выданных аккаунтов, находящихся под управлением фабрики.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Подключённые провайдеры временной почты.
    /// </summary>
    IEnumerable<TTemporaryEmailProvider> Providers { get; }

    /// <summary>
    /// Все доступные домены временной почты из подключённых провайдеров.
    /// </summary>
    IEnumerable<string> AvailableDomains { get; }

    /// <summary>
    /// Аккаунты, которые фабрика уже выдала и ещё не приняла обратно.
    /// </summary>
    IEnumerable<ITemporaryEmailAccount> Accounts { get; }

    /// <summary>
    /// Стратегия выбора следующего провайдера.
    /// </summary>
    TemporaryEmailProviderSelectionStrategy ProviderSelectionStrategy { get; set; }

    /// <summary>
    /// Создаёт новый временный почтовый аккаунт с учётом запроса.
    /// </summary>
    ValueTask<ITemporaryEmailAccount> GetAsync(TemporaryEmailAccountCreateSettings request, CancellationToken cancellationToken);

    /// <summary>
    /// Создаёт новый временный почтовый аккаунт с учётом запроса.
    /// </summary>
    ValueTask<ITemporaryEmailAccount> GetAsync(TemporaryEmailAccountCreateSettings request)
        => GetAsync(request, CancellationToken.None);

    /// <summary>
    /// Создаёт несколько временных почтовых аккаунтов с учётом запроса.
    /// </summary>
    ValueTask<IEnumerable<ITemporaryEmailAccount>> GetAsync(
        int count,
        TemporaryEmailAccountCreateSettings request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Создаёт несколько временных почтовых аккаунтов с учётом запроса.
    /// </summary>
    ValueTask<IEnumerable<ITemporaryEmailAccount>> GetAsync(int count, TemporaryEmailAccountCreateSettings request)
        => GetAsync(count, request, CancellationToken.None);

    /// <summary>
    /// Создаёт несколько временных почтовых аккаунтов.
    /// </summary>
    ValueTask<IEnumerable<ITemporaryEmailAccount>> GetAsync(int count, CancellationToken cancellationToken)
        => GetAsync(count, TemporaryEmailAccountCreateSettings.Empty, cancellationToken);

    /// <summary>
    /// Создаёт несколько временных почтовых аккаунтов.
    /// </summary>
    ValueTask<IEnumerable<ITemporaryEmailAccount>> GetAsync(int count)
        => GetAsync(count, TemporaryEmailAccountCreateSettings.Empty, CancellationToken.None);
}