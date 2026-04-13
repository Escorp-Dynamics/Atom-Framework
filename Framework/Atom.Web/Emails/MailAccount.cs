#pragma warning disable IDE0290
namespace Atom.Web.Emails;

/// <summary>
/// Класс, представляющий почтовую учётную запись.
/// </summary>
public abstract class MailAccount : IMailAccount
{
    /// <summary>
    /// Уникальный идентификатор учётной записи.
    /// </summary>
    public Guid Id { get; protected set; }

    /// <summary>
    /// Полный почтовый адрес.
    /// </summary>
    public string Address { get; protected set; }

    /// <summary>
    /// Логин почтовой учётной записи.
    /// </summary>
    public string UserName { get; protected set; }

    /// <summary>
    /// Пароль почтовой учётной записи.
    /// </summary>
    public string Password { get; protected set; }

    /// <inheritdoc/>
    public abstract IEnumerable<Mail> Inbox { get; }

    /// <inheritdoc/>
    public abstract int Count { get; }

    /// <inheritdoc/>
    public virtual bool CanSend => false;

    /// <inheritdoc/>
    public abstract event MutableEventHandler<IMailAccount, MailReceivedEventArgs>? MailReceived;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="MailAccount"/>.
    /// </summary>
    /// <param name="id">Идентификатор учётной записи.</param>
    /// <param name="login">Логин почтовой учётной записи.</param>
    /// <param name="password">Пароль почтовой учётной записи.</param>
    /// <param name="address">Полный почтовый адрес.</param>
    protected MailAccount(Guid id, string login, string password, string? address = null)
    {
        Id = id;
        UserName = login;
        Password = password;
        Address = address ?? string.Empty;
    }

    /// <inheritdoc/>
    public ValueTask ConnectAsync() => ConnectAsync(CancellationToken.None);

    /// <inheritdoc/>
    public abstract ValueTask ConnectAsync(CancellationToken cancellationToken);

    /// <inheritdoc/>
    public ValueTask DisconnectAsync() => DisconnectAsync(CancellationToken.None);

    /// <inheritdoc/>
    public abstract ValueTask DisconnectAsync(CancellationToken cancellationToken);

    /// <inheritdoc/>
    public ValueTask<IEnumerable<Mail>> RefreshInboxAsync() => RefreshInboxAsync(CancellationToken.None);

    /// <inheritdoc/>
    public abstract ValueTask<IEnumerable<Mail>> RefreshInboxAsync(CancellationToken cancellationToken);

    /// <inheritdoc/>
    public ValueTask SendAsync(Mail mail) => SendAsync(mail, CancellationToken.None);

    /// <inheritdoc/>
    public abstract ValueTask SendAsync(IMail mail, CancellationToken cancellationToken);
}
#pragma warning restore IDE0290