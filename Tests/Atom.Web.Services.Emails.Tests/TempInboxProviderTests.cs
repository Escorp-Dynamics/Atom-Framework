using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class TempInboxProviderTests(ILogger logger) : BenchmarkTests<TempInboxProviderTests>(logger)
{
    public TempInboxProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "TempInbox создаёт локальный mailbox и использует поддерживаемый домен"), Benchmark]
    public async Task TempInboxCreatesLocalMailboxAndUsesSupportedDomainTest()
    {
        using var provider = new TempInboxProvider(new TempInboxProviderOptions
        {
            SupportedDomains = ["tempinbox.com"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "tempinbox.com",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<TempInboxAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@tempinbox.com"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["tempinbox.com"]));
        }
    }

    [TestCase(TestName = "TempInbox загружает inbox из публичного JSON endpoint"), Benchmark]
    public async Task TempInboxRefreshesInboxFromPublicJsonEndpointTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/api/mailbox/atom")
            {
                return JsonResponse(
                    "[" +
                    "{" +
                    "\"id\":\"ti-1\"," +
                    "\"from\":\"sender@example.com\"," +
                    "\"subject\":\"Hello\"," +
                    "\"body\":\"Body text\"" +
                    "}" +
                    "]");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(TempInboxProvider.DefaultApiUrl),
        };

        using var provider = new TempInboxProvider(new TempInboxProviderOptions
        {
            SupportedDomains = ["tempinbox.com"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "tempinbox.com",
        }, CancellationToken.None);

        var subjects = new List<string>();
        account.MailReceived += (_, args) => subjects.Add(args.Mail.Subject);

        var snapshot = (await account.RefreshInboxAsync()).ToArray();
        var mail = snapshot.Single();

        await mail.MarkAsReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Length.EqualTo(1));
            Assert.That(mail, Is.TypeOf<TempInboxMail>());
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