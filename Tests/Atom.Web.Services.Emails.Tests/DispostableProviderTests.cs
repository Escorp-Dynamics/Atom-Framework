using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class DispostableProviderTests(ILogger logger) : BenchmarkTests<DispostableProviderTests>(logger)
{
    public DispostableProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Dispostable создаёт локальный mailbox и использует поддерживаемые домены"), Benchmark]
    public async Task DispostableCreatesLocalMailboxAndUsesSupportedDomainsTest()
    {
        using var provider = new DispostableProvider(new DispostableProviderOptions
        {
            SupportedDomains = ["dispostable.com"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "dispostable.com",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<DispostableAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@dispostable.com"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["dispostable.com"]));
        }
    }

    [TestCase(TestName = "Dispostable загружает inbox из plain text feed и поддерживает delete"), Benchmark]
    public async Task DispostableRefreshesInboxFromPlainTextFeedTest()
    {
        var deletedIds = new List<string>();

        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/mailbox/atom.txt")
            {
                return PlainTextResponse("dp-1|sender@example.com|Hello|Body text\n");
            }

            if (request.Method == HttpMethod.Delete && request.RequestUri?.PathAndQuery == "/mailbox/atom/dp-1")
            {
                deletedIds.Add("dp-1");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(DispostableProvider.DefaultApiUrl),
        };

        using var provider = new DispostableProvider(new DispostableProviderOptions
        {
            SupportedDomains = ["dispostable.com"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "dispostable.com",
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
            Assert.That(mail, Is.TypeOf<DispostableMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello"]));
            Assert.That(deletedIds, Is.EqualTo(["dp-1"]));
        }
    }

    private static HttpResponseMessage PlainTextResponse(string payload)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/plain"),
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}