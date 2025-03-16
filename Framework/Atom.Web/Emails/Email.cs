namespace Atom.Web.Emails;

/// <summary>
/// Представляет сообщение электронной почты.
/// </summary>
public class Email
{
    /// <summary>
    /// Адрес электронной почты отправителя.
    /// </summary>
    public string From { get; protected set; }

    /// <summary>
    /// Адрес электронной почты получателя.
    /// </summary>
    public string To { get; protected set; }

    /// <summary>
    /// Тема электронного письма.
    /// </summary>
    public string Subject { get; protected set; }

    /// <summary>
    /// Тело электронного письма.
    /// </summary>
    public string Body { get; protected set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="Email"/>.
    /// </summary>
    /// <param name="from">Адрес электронной почты отправителя.</param>
    /// <param name="to">Адрес электронной почты получателя.</param>
    /// <param name="subject">Тема электронного письма.</param>
    /// <param name="body">Тело электронного письма.</param>
    /// <returns></returns>
    public Email(string from, string to, string subject, string body) => (From, To, Subject, Body) = (from, to, subject, body);
}