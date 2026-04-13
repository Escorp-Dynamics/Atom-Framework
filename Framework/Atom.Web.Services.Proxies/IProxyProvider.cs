using Microsoft.Extensions.Logging;
using Atom.Web.Services;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Представляет базовый интерфейс для реализации fetch-only провайдеров прокси.
/// </summary>
public interface IProxyProvider : IWebService
{
    /// <summary>
    /// Внешний логгер для provider-level runtime diagnostics.
    /// </summary>
    ILogger? Logger { get; set; }

    /// <summary>
    /// Возвращает актуальный снимок прокси, полученный и нормализованный из внешнего источника провайдера.
    /// </summary>
    ValueTask<IEnumerable<ServiceProxy>> FetchAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Возвращает актуальный снимок прокси, полученный и нормализованный из внешнего источника провайдера.
    /// </summary>
    ValueTask<IEnumerable<ServiceProxy>> FetchAsync() => FetchAsync(CancellationToken.None);

    /// <summary>
    /// Коллекция подключённых валидаторов прокси.
    /// </summary>
    IEnumerable<IProxyValidator> Validators { get; }

    /// <summary>
    /// Добавляет валидатор прокси.
    /// </summary>
    /// <param name="validator">Валидатор прокси.</param>
    /// <typeparam name="T">Тип валидатора прокси.</typeparam>
    IProxyProvider UseValidator<T>(T validator) where T : IProxyValidator;

    /// <summary>
    /// Добавляет валидатор прокси.
    /// </summary>
    /// <typeparam name="T">Тип валидатора прокси.</typeparam>
    IProxyProvider UseValidator<T>() where T : IProxyValidator, new();

    /// <summary>
    /// Валидирует прокси подключёнными к сервису валидаторами.
    /// </summary>
    /// <param name="proxy">Прокси, который нужно проверить.</param>
    /// <param name="url">Ссылка для проверки.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    async ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        ArgumentNullException.ThrowIfNull(url);

        foreach (var validator in Validators)
        {
            if (!await validator.ValidateAsync(proxy, url, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Валидирует прокси подключёнными к сервису валидаторами.
    /// </summary>
    /// <param name="proxy">Прокси, который нужно проверить.</param>
    /// <param name="url">Ссылка для проверки.</param>
    ValueTask<bool> ValidateAsync(ServiceProxy proxy, Uri url) => ValidateAsync(proxy, url, CancellationToken.None);
}