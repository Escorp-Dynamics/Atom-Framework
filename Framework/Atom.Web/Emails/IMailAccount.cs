namespace Atom.Web.Emails;

/// <summary>
/// Представляет базовый контракт почтовой учётной записи.
/// </summary>
public interface IMailAccount
{
    /// <summary>
    /// Уникальный идентификатор учётной записи.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// Полный почтовый адрес.
    /// </summary>
    string Address { get; }

    /// <summary>
    /// Логин почтовой учётной записи.
    /// </summary>
    string UserName { get; }

    /// <summary>
    /// Пароль почтовой учётной записи.
    /// </summary>
    string Password { get; }

    /// <summary>
    /// Последний известный snapshot входящих писем.
    /// </summary>
    IEnumerable<Mail> Inbox { get; }

    /// <summary>
    /// Число писем в последнем известном snapshot inbox.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Показывает, поддерживает ли аккаунт исходящую отправку писем.
    /// </summary>
    bool CanSend { get; }

    /// <summary>
    /// Возникает при получении нового письма.
    /// </summary>
    event MutableEventHandler<IMailAccount, MailReceivedEventArgs>? MailReceived;

    /// <summary>
    /// Устанавливает соединение с почтовым сервером.
    /// </summary>
    ValueTask ConnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Устанавливает соединение с почтовым сервером.
    /// </summary>
    ValueTask ConnectAsync() => ConnectAsync(CancellationToken.None);

    /// <summary>
    /// Закрывает соединение с почтовым сервером.
    /// </summary>
    ValueTask DisconnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Закрывает соединение с почтовым сервером.
    /// </summary>
    ValueTask DisconnectAsync() => DisconnectAsync(CancellationToken.None);

    /// <summary>
    /// Обновляет входящие письма.
    /// </summary>
    ValueTask<IEnumerable<Mail>> RefreshInboxAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Обновляет входящие письма.
    /// </summary>
    ValueTask<IEnumerable<Mail>> RefreshInboxAsync() => RefreshInboxAsync(CancellationToken.None);

    /// <summary>
    /// Отправляет письмо через аккаунт.
    /// </summary>
    ValueTask SendAsync(IMail mail, CancellationToken cancellationToken);

    /// <summary>
    /// Отправляет письмо через аккаунт.
    /// </summary>
    ValueTask SendAsync(IMail mail) => SendAsync(mail, CancellationToken.None);
}