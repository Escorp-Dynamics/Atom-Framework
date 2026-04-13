namespace Atom.Web.Emails.Tests;

internal interface ILiveMailDeliverySender
{
    ValueTask SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken);
}