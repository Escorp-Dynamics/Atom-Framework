using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class SpamboxProviderTests(ILogger logger) : BenchmarkTests<SpamboxProviderTests>(logger)
{
    public SpamboxProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Spambox создаёт локальный mailbox и использует поддерживаемый домен"), Benchmark]
    public async Task SpamboxCreatesLocalMailboxAndUsesSupportedDomainTest()
    {
        using var provider = new SpamboxProvider(new SpamboxProviderOptions
        {
            SupportedDomains = ["spambox.xyz"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "spambox.xyz",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<SpamboxAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@spambox.xyz"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["spambox.xyz"]));
        }
    }

    [TestCase(TestName = "Spambox загружает inbox из публичного JSON endpoint"), Benchmark]
    public async Task SpamboxRefreshesInboxFromPublicJsonEndpointTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/api/messages/atom")
            {
                return JsonResponse(
                    "[" +
                    "{" +
                    "\"id\":\"sb-1\"," +
                    "\"from\":\"sender@example.com\"," +
                    "\"subject\":\"Hello\"," +
                    "\"text\":\"Body text\"" +
                    "}" +
                    "]");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(SpamboxProvider.DefaultApiUrl),
        };

        using var provider = new SpamboxProvider(new SpamboxProviderOptions
        {
            SupportedDomains = ["spambox.xyz"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "spambox.xyz",
        }, CancellationToken.None);

        var subjects = new List<string>();
        account.MailReceived += (_, args) => subjects.Add(args.Mail.Subject);

        var snapshot = (await account.RefreshInboxAsync()).ToArray();
        var mail = snapshot.Single();

        await mail.MarkAsReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Length.EqualTo(1));
            Assert.That(mail, Is.TypeOf<SpamboxMail>());
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