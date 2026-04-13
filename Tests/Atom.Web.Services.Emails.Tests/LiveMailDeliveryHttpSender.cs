using System.Net.Http.Headers;

namespace Atom.Web.Emails.Tests;

internal sealed class LiveMailDeliveryHttpSender(LiveMailDeliveryHttpSenderOptions options) : ILiveMailDeliverySender
{
    public async ValueTask SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
        {
            Content = new FormUrlEncodedContent(CreatePayload(toAddress, subject, body)),
        };

        if (!string.IsNullOrWhiteSpace(options.Authorization))
        {
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(options.Authorization);
        }

        using var client = new HttpClient();
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private IEnumerable<KeyValuePair<string, string>> CreatePayload(string toAddress, string subject, string body)
    {
        yield return new KeyValuePair<string, string>("to", toAddress);
        yield return new KeyValuePair<string, string>("subject", subject);
        yield return new KeyValuePair<string, string>("body", body);

        if (!string.IsNullOrWhiteSpace(options.FromAddress))
        {
            yield return new KeyValuePair<string, string>("from", options.FromAddress);
        }

        if (!string.IsNullOrWhiteSpace(options.FromDisplayName))
        {
            yield return new KeyValuePair<string, string>("fromName", options.FromDisplayName);
        }
    }
}