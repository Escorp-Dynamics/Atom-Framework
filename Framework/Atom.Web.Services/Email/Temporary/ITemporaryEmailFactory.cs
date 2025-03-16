/*namespace Atom.Web.Services.Email.Temporary;

/// <summary>
/// Представляет базовый интерфейс для реализации фабрики временных почт.
/// </summary>
public interface ITemporaryEmailFactory : IWebServiceFactory<ITemporaryEmailFactory, ITemporaryEmailService>
{
    /// <summary>
    /// Асинхронно получает следующий домен временной почты.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены для отмены операции.</param>
    /// <returns>Возвращает задачу, представляющую асинхронную операцию получения следующего домена временной почты.
    /// Возвращает null, если домен не может быть получен.</returns>
    ValueTask<string?> GetNextDomainAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Асинхронно получает следующий домен временной почты.
    /// </summary>
    /// <returns>Возвращает задачу, представляющую асинхронную операцию получения следующего домена временной почты.
    /// Возвращает null, если домен не может быть получен.</returns>
    ValueTask<string?> GetNextDomainAsync() => GetNextDomainAsync(CancellationToken.None);

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