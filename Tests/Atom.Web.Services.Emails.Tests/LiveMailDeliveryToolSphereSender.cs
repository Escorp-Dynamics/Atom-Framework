using System.Buffers;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Atom.Web.Emails.Tests;

internal sealed class LiveMailDeliveryToolSphereSender(LiveMailDeliveryToolSphereSenderOptions options) : ILiveMailDeliverySender
{
    private const string Endpoint = "https://mail-sender-api1.p.rapidapi.com/";
    private const string HostHeader = "mail-sender-api1.p.rapidapi.com";

    public async ValueTask SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = CreateJsonContent(toAddress, subject, body),
        };

        request.Headers.Add("X-RapidAPI-Key", options.ApiKey);
        request.Headers.Add("X-RapidAPI-Host", HostHeader);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var client = new HttpClient();
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Tool Sphere sender вернул HTTP {(int)response.StatusCode}: {responseText}");
        }

        if (responseText.Contains("not subscribed to this API", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Tool Sphere sender требует бесплатную подписку на API в RapidAPI для заданного ключа.");
        }
    }

    private StringContent CreateJsonContent(string toAddress, string subject, string body)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("sendto", toAddress);
            writer.WriteString("ishtml", "false");
            writer.WriteString("title", subject);
            writer.WriteString("body", body);
            writer.WriteString("name", options.SenderName);
            writer.WriteEndObject();
        }

        return new StringContent(Encoding.UTF8.GetString(buffer.WrittenSpan), Encoding.UTF8, "application/json");
    }
}