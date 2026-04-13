using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class MinuteInboxProviderTests(ILogger logger) : BenchmarkTests<MinuteInboxProviderTests>(logger)
{
    public MinuteInboxProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "MinuteInbox создаёт локальный mailbox и использует поддерживаемый домен"), Benchmark]
    public async Task MinuteInboxCreatesLocalMailboxAndUsesSupportedDomainTest()
    {
        using var provider = new MinuteInboxProvider(new MinuteInboxProviderOptions
        {
            SupportedDomains = ["minuteinbox.com"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "minuteinbox.com",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<MinuteInboxAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@minuteinbox.com"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["minuteinbox.com"]));
        }
    }

    [TestCase(TestName = "MinuteInbox загружает inbox из публичного JSON endpoint"), Benchmark]
    public async Task MinuteInboxRefreshesInboxFromPublicJsonEndpointTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/api/messages/atom")
            {
                return JsonResponse(
                    "[" +
                    "{" +
                    "\"id\":\"mi-1\"," +
                    "\"from\":\"sender@example.com\"," +
                    "\"subject\":\"Hello\"," +
                    "\"text\":\"Body text\"" +
                    "}" +
                    "]");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(MinuteInboxProvider.DefaultApiUrl),
        };

        using var provider = new MinuteInboxProvider(new MinuteInboxProviderOptions
        {
            SupportedDomains = ["minuteinbox.com"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "minuteinbox.com",
        }, CancellationToken.None);

        var subjects = new List<string>();
        account.MailReceived += (_, args) => subjects.Add(args.Mail.Subject);

        var snapshot = (await account.RefreshInboxAsync()).ToArray();
        var mail = snapshot.Single();

        await mail.MarkAsReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Length.EqualTo(1));
            Assert.That(mail, Is.TypeOf<MinuteInboxMail>());
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