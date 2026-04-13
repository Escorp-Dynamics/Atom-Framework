using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class MailCatchProviderTests(ILogger logger) : BenchmarkTests<MailCatchProviderTests>(logger)
{
    public MailCatchProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "MailCatch создаёт локальный mailbox и использует поддерживаемый домен"), Benchmark]
    public async Task MailCatchCreatesLocalMailboxAndUsesSupportedDomainTest()
    {
        using var provider = new MailCatchProvider(new MailCatchProviderOptions
        {
            SupportedDomains = ["mailcatch.com"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "mailcatch.com",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<MailCatchAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@mailcatch.com"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["mailcatch.com"]));
        }
    }

    [TestCase(TestName = "MailCatch загружает inbox из публичного JSON endpoint"), Benchmark]
    public async Task MailCatchRefreshesInboxFromPublicJsonEndpointTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/api/inbox/atom")
            {
                return JsonResponse(
                    "[" +
                    "{" +
                    "\"id\":\"mc-1\"," +
                    "\"from\":\"sender@example.com\"," +
                    "\"subject\":\"Hello\"," +
                    "\"body\":\"Body text\"" +
                    "}" +
                    "]");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(MailCatchProvider.DefaultApiUrl),
        };

        using var provider = new MailCatchProvider(new MailCatchProviderOptions
        {
            SupportedDomains = ["mailcatch.com"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "mailcatch.com",
        }, CancellationToken.None);

        var subjects = new List<string>();
        account.MailReceived += (_, args) => subjects.Add(args.Mail.Subject);

        var snapshot = (await account.RefreshInboxAsync()).ToArray();
        var mail = snapshot.Single();

        await mail.MarkAsReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Length.EqualTo(1));
            Assert.That(mail, Is.TypeOf<MailCatchMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello"]));
        }
    }

    private static HttpResponseMessage JsonResponse(string payload)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}