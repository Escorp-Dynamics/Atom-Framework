using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class EmailOnDeckProviderTests(ILogger logger) : BenchmarkTests<EmailOnDeckProviderTests>(logger)
{
    public EmailOnDeckProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "EmailOnDeck создаёт mailbox через form POST"), Benchmark]
    public async Task EmailOnDeckCreatesMailboxViaFormPostTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.PathAndQuery == "/api/create")
            {
                var body = await request.Content!.ReadAsStringAsync();

                Assert.That(body, Does.Contain("name=custom"));
                Assert.That(body, Does.Contain("domain=emailondeck.com"));

                return JsonResponse(
                    "{" +
                    "\"email\":\"custom@emailondeck.com\"," +
                    "\"sessionToken\":\"session-1\"" +
                    "}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(EmailOnDeckProvider.DefaultApiUrl),
        };

        using var provider = new EmailOnDeckProvider(new EmailOnDeckProviderOptions
        {
            SupportedDomains = ["emailondeck.com"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "emailondeck.com",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<EmailOnDeckAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@emailondeck.com"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["emailondeck.com"]));
        }
    }

    [TestCase(TestName = "EmailOnDeck загружает inbox через form POST и поддерживает delete"), Benchmark]
    public async Task EmailOnDeckRefreshesInboxAndDeletesMailTest()
    {
        var deletedIds = new List<string>();

        using var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();

            if (request.Method == HttpMethod.Post && request.RequestUri?.PathAndQuery == "/api/create")
            {
                return JsonResponse(
                    "{" +
                    "\"email\":\"atom@emailondeck.com\"," +
                    "\"sessionToken\":\"session-2\"" +
                    "}");
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.PathAndQuery == "/api/inbox")
            {
                Assert.That(body, Does.Contain("sessionToken=session-2"));

                return JsonResponse(
                    "[" +
                    "{" +
                    "\"id\":\"901\"," +
                    "\"from\":\"sender@example.com\"," +
                    "\"subject\":\"Hello\"," +
                    "\"body\":\"Body text\"," +
                    "\"read\":false" +
                    "}" +
                    "]");
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.PathAndQuery == "/api/delete")
            {
                if (body.Contains("messageId=901", StringComparison.Ordinal))
                {
                    deletedIds.Add("901");
                }

                return JsonResponse("{" + "\"deleted\":true" + "}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(EmailOnDeckProvider.DefaultApiUrl),
        };

        using var provider = new EmailOnDeckProvider(new EmailOnDeckProviderOptions
        {
            SupportedDomains = ["emailondeck.com"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "emailondeck.com",
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
            Assert.That(mail, Is.TypeOf<EmailOnDeckMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello"]));
            Assert.That(deletedIds, Is.EqualTo(["901"]));
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