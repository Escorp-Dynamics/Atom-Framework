/*namespace Atom.Web.Services.Email.Temporary;

/// <summary>
/// Представляет базовый интерфейс для реализации сервисов временной почты.
/// </summary>
public interface ITemporaryEmailService : IWebService
{
    /// <summary>
    /// Коллекция доступных доменов.
    /// </summary>
    IEnumerable<string> Domains { get; }

    /// <summary>
    /// Асинхронно получает список доступных доменов.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены для отмены операции.</param>
    /// <returns>Асинхронный перечислитель строк, представляющих доступные домены.</returns>
    IAsyncEnumerable<string> GetDomainsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Асинхронно получает список доступных доменов.
    /// </summary>
    /// <returns>Асинхронный перечислитель строк, представляющих доступные домены.</returns>
    IAsyncEnumerable<string> GetDomainsAsync() => GetDomainsAsync(CancellationToken.None);

    /// <summary>
    /// Асинхронно создает новый аккаунт с указанным логином и паролем.
    /// </summary>
    /// <param name="login">Логин для создания аккаунта.</param>
    /// <param name="password">Пароль для создания аккаунта.</param>
    /// <param name="cancellationToken">Токен отмены для отмены операции.</param>
    /// <returns>Возвращает задачу, представляющую асинхронную операцию создания аккаунта.
    /// Возвращает true, если аккаунт успешно создан, иначе false.</returns>
    ValueTask<TemporaryEmailAccount?> CreateAccountAsync(string login, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Асинхронно создает новый аккаунт с указанным логином и паролем.
    /// </summary>
    /// <param name="login">Логин для создания аккаунта.</param>
    /// <param name="password">Пароль для создания аккаунта.</param>
    /// <returns>Возвращает задачу, представляющую асинхронную операцию создания аккаунта.
    /// Возвращает true, если аккаунт успешно создан, иначе false.</returns>
    ValueTask<TemporaryEmailAccount?> CreateAccountAsync(string login, string password) => CreateAccountAsync(login, password, CancellationToken.None);
}*/