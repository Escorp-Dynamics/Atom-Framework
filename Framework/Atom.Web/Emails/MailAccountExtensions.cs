namespace Atom.Web.Emails;

/// <summary>
/// Вспомогательные расширения для безопасной работы с почтовыми аккаунтами.
/// </summary>
public static class MailAccountExtensions
{
    /// <summary>
    /// Проверяет, что аккаунт поддерживает исходящую отправку писем.
    /// </summary>
    /// <exception cref="ArgumentNullException">Если аккаунт не задан.</exception>
    /// <exception cref="NotSupportedException">Если аккаунт не поддерживает отправку.</exception>
    public static void EnsureCanSend(this IMailAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (!account.CanSend)
        {
            throw new NotSupportedException($"Почтовый аккаунт '{account.Address}' не поддерживает исходящую отправку.");
        }
    }

    /// <summary>
    /// Отправляет письмо только если аккаунт поддерживает исходящую отправку.
    /// </summary>
    /// <exception cref="ArgumentNullException">Если аккаунт или письмо не заданы.</exception>
    /// <exception cref="NotSupportedException">Если аккаунт не поддерживает отправку.</exception>
    public static ValueTask SendCheckedAsync(this IMailAccount account, IMail mail, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(mail);

        account.EnsureCanSend();
        return account.SendAsync(mail, cancellationToken);
    }
}