using System.Net;
using System.Text;
using System.Text.Json;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class EmailnatorProviderTests(ILogger logger) : BenchmarkTests<EmailnatorProviderTests>(logger)
{
    public EmailnatorProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Emailnator создаёт mailbox через JSON session API"), Benchmark]
    public async Task EmailnatorCreatesMailboxViaJsonSessionApiTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.PathAndQuery == "/generate-email")
            {
                var body = await request.Content!.ReadAsStringAsync();
                using var document = JsonDocument.Parse(body);

                Assert.That(document.RootElement.GetProperty("name").GetString(), Is.EqualTo("custom"));
                Assert.That(document.RootElement.GetProperty("domain").GetString(), Is.EqualTo("emailnator.com"));

                return JsonResponse(
                    "{" +
                    "\"email\":\"custom@emailnator.com\"," +
                    "\"sessionId\":\"session-1\"" +
                    "}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(EmailnatorProvider.DefaultApiUrl),
        };

        using var provider = new EmailnatorProvider(new EmailnatorProviderOptions
        {
            SupportedDomains = ["emailnator.com"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "emailnator.com",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<EmailnatorAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@emailnator.com"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["emailnator.com"]));
        }
    }

    [TestCase(TestName = "Emailnator загружает inbox через JSON POST и поддерживает delete"), Benchmark]
    public async Task EmailnatorRefreshesInboxAndDeletesMailTest()
    {
        var deletedIds = new List<string>();

        using var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);

            if (request.Method == HttpMethod.Post && request.RequestUri?.PathAndQuery == "/generate-email")
            {
                return JsonResponse(
                    "{" +
                    "\"email\":\"atom@emailnator.com\"," +
                    "\"sessionId\":\"session-2\"" +
                    "}");
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.PathAndQuery == "/message-list")
            {
                Assert.That(document.RootElement.GetProperty("sessionId").GetString(), Is.EqualTo("session-2"));

                return JsonResponse(
                    "{" +
                    "\"messages\":[{" +
                    "\"messageId\":\"501\"," +
                    "\"from\":\"sender@example.com\"," +
                    "\"subject\":\"Hello\"" +
                    "}]" +
                    "}");
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.PathAndQuery == "/message-detail")
            {
                Assert.That(document.RootElement.GetProperty("messageId").GetString(), Is.EqualTo("501"));

                return JsonResponse(
                    "{" +
                    "\"messageId\":\"501\"," +
                    "\"from\":\"sender@example.com\"," +
                    "\"subject\":\"Hello\"," +
                    "\"content\":\"Body text\"" +
                    "}");
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.PathAndQuery == "/delete-message")
            {
                deletedIds.Add(document.RootElement.GetProperty("messageId").GetString() ?? string.Empty);
                return JsonResponse("{" + "\"deleted\":true" + "}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(EmailnatorProvider.DefaultApiUrl),
        };

        using var provider = new EmailnatorProvider(new EmailnatorProviderOptions
        {
            SupportedDomains = ["emailnator.com"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "emailnator.com",
        }, CancellationToken.None);

        var subjects = new List<string>();
        account.MailReceived += (_, args) => subjects.Add(args.Mail.Subject);

        var snapshot = (await account.RefreshInboxAsync()).ToArray();
        var mail = snapshot.Single();

        await mail.MarkAsReadAsync();
        await mail.DeleteAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Length.EqualTo(1));
            Assert.That(mail, Is.TypeOf<EmailnatorMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello"]));
            Assert.That(deletedIds, Is.EqualTo(["501"]));
        }
    }

    private static HttpResponseMessage JsonResponse(string payload)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request);
    }
}