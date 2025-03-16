using Atom.Web.Services;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Представляет базовый интерфейс для реализации сервисов прокси.
/// </summary>
public interface IProxyService : IWebService
{
    /// <summary>
    /// Коллекция подключённых валидаторов прокси.
    /// </summary>
    IEnumerable<IProxyValidator> Validators { get; }

    /// <summary>
    /// Добавляет валидатор прокси.
    /// </summary>
    /// <param name="validator">Валидатор прокси.</param>
    /// <typeparam name="T">Тип валидатора прокси.</typeparam>
    IProxyService UseValidator<T>(T validator) where T : IProxyValidator;

    /// <summary>
    /// Добавляет валидатор прокси.
    /// </summary>
    /// <typeparam name="T">Тип валидатора прокси.</typeparam>
    IProxyService UseValidator<T>() where T : IProxyValidator, new();

    /// <summary>
    /// Получает прокси.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask<ServiceProxy> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Получает прокси.
    /// </summary>
    ValueTask<ServiceProxy> GetAsync() => GetAsync(CancellationToken.None);
}