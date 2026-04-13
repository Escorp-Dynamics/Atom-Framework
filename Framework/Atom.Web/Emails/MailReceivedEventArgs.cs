using System.Diagnostics.CodeAnalysis;

namespace Atom.Web.Emails;

/// <summary>
/// Содержит новое письмо, полученное аккаунтом.
/// </summary>
/// <param name="mail">Новое письмо.</param>
public sealed class MailReceivedEventArgs([NotNull] Mail mail) : EventArgs
{
    /// <summary>
    /// Новое письмо.
    /// </summary>
    public Mail Mail { get; } = mail;
}