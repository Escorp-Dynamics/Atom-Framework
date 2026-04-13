using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class FakeMailProviderTests(ILogger logger) : BenchmarkTests<FakeMailProviderTests>(logger)
{
    public FakeMailProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "FakeMail создаёт локальный mailbox и использует поддерживаемый домен"), Benchmark]
    public async Task FakeMailCreatesLocalMailboxAndUsesSupportedDomainTest()
    {
        using var provider = new FakeMailProvider(new FakeMailProviderOptions
        {
            SupportedDomains = ["fakemail.net"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "fakemail.net",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<FakeMailAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@fakemail.net"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["fakemail.net"]));
        }
    }

    [TestCase(TestName = "FakeMail загружает inbox из публичного JSON endpoint"), Benchmark]
    public async Task FakeMailRefreshesInboxFromPublicJsonEndpointTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/api/inbox/atom")
            {
                return JsonResponse(
                    "[" +
                    "{" +
                    "\"id\":\"fm-1\"," +
                    "\"from\":\"sender@example.com\"," +
                    "\"subject\":\"Hello from FakeMail\"," +
                    "\"body\":\"Body text\"" +
                    "}" +
                    "]");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(FakeMailProvider.DefaultApiUrl),
        };

        using var provider = new FakeMailProvider(new FakeMailProviderOptions
        {
            SupportedDomains = ["fakemail.net"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "fakemail.net",
        }, CancellationToken.None);

        var subjects = new List<string>();
        account.MailReceived += (_, args) => subjects.Add(args.Mail.Subject);

        var snapshot = (await account.RefreshInboxAsync()).ToArray();
        var mail = snapshot.Single();

        await mail.MarkAsReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Length.EqualTo(1));
            Assert.That(mail, Is.TypeOf<FakeMailMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello from FakeMail"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello from FakeMail"]));
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