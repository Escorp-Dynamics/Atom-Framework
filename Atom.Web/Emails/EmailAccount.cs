namespace Atom.Web.Emails;

/// <summary>
/// Класс, представляющий учетную запись электронной почты.
/// </summary>
public class EmailAccount
{
    /// <summary>
    /// Логин электронной почты.
    /// </summary>
    public string Login { get; protected set; }

    /// <summary>
    /// Пароль электронной почты.
    /// </summary>
    public string Password { get; protected set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="EmailAccount"/>.
    /// </summary>
    /// <param name="login">Логин электронной почты.</param>
    /// <param name="password">Пароль электронной почты.</param>
    /// <returns></returns>
    public EmailAccount(string login, string password) => (Login, Password) = (login, password);
}