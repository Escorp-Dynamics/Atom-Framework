using System.Net;
using System.Net.Mail;

namespace Atom.Web.Emails.Tests;

internal sealed class LiveMailDeliverySmtpClient(LiveMailDeliverySmtpOptions options) : ILiveMailDeliverySender
{
    public async ValueTask SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        using var message = new MailMessage
        {
            From = CreateFromAddress(),
            Subject = subject,
            Body = body,
            IsBodyHtml = false,
        };

        message.To.Add(toAddress);

        using var client = new SmtpClient(options.Host, options.Port)
        {
            EnableSsl = options.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };

        if (!string.IsNullOrWhiteSpace(options.UserName))
        {
            client.Credentials = new NetworkCredential(options.UserName, options.Password ?? string.Empty);
        }

        await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private MailAddress CreateFromAddress()
        => string.IsNullOrWhiteSpace(options.FromDisplayName)
            ? new MailAddress(options.FromAddress)
            : new MailAddress(options.FromAddress, options.FromDisplayName);
}