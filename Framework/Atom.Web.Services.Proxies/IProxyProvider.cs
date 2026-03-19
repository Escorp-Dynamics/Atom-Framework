using Atom.Architect.Factories;
using Atom.Web.Services;

namespace Atom.Web.Proxies.Services;

/// <summary>
/// Представляет базовый интерфейс для реализации провайдеров прокси.
/// </summary>
public interface IProxyProvider : IWebService, IAsyncFactory<ServiceProxy>
{
    /// <summary>
    /// Стратегия выбора следующего прокси из внутреннего пула.
    /// Для <see cref="ProxyRotationStrategy.Random"/> выборка строится независимо для каждого вызова.
    /// </summary>
    ProxyRotationStrategy RotationStrategy { get; set; }

    /// <summary>
    /// Интервал автоматического обновления внутреннего пула прокси.
    /// </summary>
    TimeSpan RefreshInterval { get; set; }

    /// <summary>
    /// Интервал ожидания перед повторной попыткой фонового обновления после ошибки.
    /// </summary>
    TimeSpan RefreshErrorBackoff { get; set; }

    /// <summary>
    /// Указывает, нужно ли сохранять последний успешный пул при ошибке обновления.
    /// </summary>
    bool PreservePoolOnRefreshFailure { get; set; }

    /// <summary>
    /// Время последнего успешного обновления внутреннего пула прокси.
    /// </summary>
    DateTime LastRefreshUtc { get; }

    /// <summary>
    /// Последняя ошибка обновления внутреннего пула.
    /// </summary>
    Exception? LastRefreshException { get; }

    /// <summary>
    /// Текущее количество прокси во внутреннем пуле.
    /// </summary>
    int PoolCount { get; }

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
    /// Принудительно обновляет внутренний пул прокси.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask RefreshAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Принудительно обновляет внутренний пул прокси.
    /// </summary>
    ValueTask RefreshAsync() => RefreshAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает следующий прокси из внутреннего пула.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    new ValueTask<ServiceProxy> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Возвращает следующий прокси из внутреннего пула.
    /// </summary>
    new ValueTask<ServiceProxy> GetAsync() => GetAsync(CancellationToken.None);

    /// <summary>
    /// Возвращает следующий прокси из внутреннего пула, удовлетворяющий фильтру.
    /// </summary>
    /// <param name="filter">Фильтр выборки прокси.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask<ServiceProxy> GetAsync(Func<ServiceProxy, bool> filter, CancellationToken cancellationToken);

    /// <summary>
    /// Возвращает следующий прокси из внутреннего пула, удовлетворяющий фильтру.
    /// </summary>
    /// <param name="filter">Фильтр выборки прокси.</param>
    ValueTask<ServiceProxy> GetAsync(Func<ServiceProxy, bool> filter) => GetAsync(filter, CancellationToken.None);

    /// <summary>
    /// Возвращает следующую последовательность прокси из внутреннего пула.
    /// </summary>
    /// <param name="count">Требуемое количество элементов.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask<IEnumerable<ServiceProxy>> GetAsync(int count, CancellationToken cancellationToken);

    /// <summary>
    /// Возвращает следующую последовательность прокси из внутреннего пула.
    /// </summary>
    /// <param name="count">Требуемое количество элементов.</param>
    ValueTask<IEnumerable<ServiceProxy>> GetAsync(int count) => GetAsync(count, CancellationToken.None);

    /// <summary>
    /// Возвращает следующую последовательность прокси из внутреннего пула, удовлетворяющую фильтру.
    /// </summary>
    /// <param name="count">Требуемое количество элементов.</param>
    /// <param name="filter">Фильтр выборки прокси.</param>
    /// <param name="cancellationToken">Токен отмены задачи.</param>
    ValueTask<IEnumerable<ServiceProxy>> GetAsync(int count, Func<ServiceProxy, bool> filter, CancellationToken cancellationToken);

    /// <summary>
    /// Возвращает следующую последовательность прокси из внутреннего пула, удовлетворяющую фильтру.
    /// </summary>
    /// <param name="count">Требуемое количество элементов.</param>
    /// <param name="filter">Фильтр выборки прокси.</param>
    ValueTask<IEnumerable<ServiceProxy>> GetAsync(int count, Func<ServiceProxy, bool> filter) => GetAsync(count, filter, CancellationToken.None);

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

    /// <summary>
    /// Возвращает прокси обратно в фабричный контракт.
    /// Для текущей модели прокси не арендуются из object pool, поэтому операция является безопасным no-op.
    /// </summary>
    /// <param name="item">Возвращаемый прокси.</param>
    new void Return(ServiceProxy item)
    {
        ArgumentNullException.ThrowIfNull(item);
    }
}