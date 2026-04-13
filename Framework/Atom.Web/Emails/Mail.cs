#pragma warning disable CA1724
namespace Atom.Web.Emails;

/// <summary>
/// Представляет базовое почтовое сообщение.
/// </summary>
public class Mail(Guid id, string from, string to, string subject, string body) : IMail
{
    /// <summary>
    /// Уникальный идентификатор письма.
    /// </summary>
    public Guid Id { get; protected set; } = id;

    /// <summary>
    /// Адрес отправителя.
    /// </summary>
    public string From { get; protected set; } = from;

    /// <summary>
    /// Адрес получателя.
    /// </summary>
    public string To { get; protected set; } = to;

    /// <summary>
    /// Тема письма.
    /// </summary>
    public string Subject { get; protected set; } = subject;

    /// <summary>
    /// Тело письма.
    /// </summary>
    public string Body { get; protected set; } = body;

    /// <summary>
    /// Признак того, что письмо уже прочитано.
    /// </summary>
    public bool IsRead { get; protected set; }

    /// <summary>
    /// Инициализирует новое сообщение с автоматически созданным идентификатором.
    /// </summary>
    public Mail(string from, string to, string subject, string body)
        : this(Guid.NewGuid(), from, to, subject, body) { }

    /// <inheritdoc/>
    public virtual ValueTask MarkAsReadAsync(CancellationToken cancellationToken)
    {
        IsRead = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Помечает письмо как прочитанное.
    /// </summary>
    public ValueTask MarkAsReadAsync() => MarkAsReadAsync(CancellationToken.None);

    /// <inheritdoc/>
    public virtual ValueTask DeleteAsync(CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    /// <summary>
    /// Удаляет письмо.
    /// </summary>
    public ValueTask DeleteAsync() => DeleteAsync(CancellationToken.None);
}
#pragma warning restore CA1724