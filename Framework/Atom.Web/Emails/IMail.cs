#pragma warning disable CA1716
namespace Atom.Web.Emails;

/// <summary>
/// Представляет базовый контракт почтового сообщения.
/// </summary>
public interface IMail
{
    /// <summary>
    /// Уникальный идентификатор письма.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// Адрес отправителя.
    /// </summary>
    string From { get; }

    /// <summary>
    /// Адрес получателя.
    /// </summary>
    string To { get; }

    /// <summary>
    /// Тема письма.
    /// </summary>
    string Subject { get; }

    /// <summary>
    /// Тело письма.
    /// </summary>
    string Body { get; }

    /// <summary>
    /// Признак того, что письмо уже прочитано.
    /// </summary>
    bool IsRead { get; }

    /// <summary>
    /// Помечает письмо как прочитанное.
    /// </summary>
    ValueTask MarkAsReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Помечает письмо как прочитанное.
    /// </summary>
    ValueTask MarkAsReadAsync() => MarkAsReadAsync(CancellationToken.None);

    /// <summary>
    /// Удаляет письмо.
    /// </summary>
    ValueTask DeleteAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Удаляет письмо.
    /// </summary>
    ValueTask DeleteAsync() => DeleteAsync(CancellationToken.None);
}
#pragma warning restore CA1716