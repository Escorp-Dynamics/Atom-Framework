using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class MaildropProviderTests(ILogger logger) : BenchmarkTests<MaildropProviderTests>(logger)
{
    public MaildropProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Maildrop создаёт локальный mailbox и использует поддерживаемый домен"), Benchmark]
    public async Task MaildropCreatesLocalMailboxAndUsesSupportedDomainTest()
    {
        using var provider = new MaildropProvider(new MaildropProviderOptions
        {
            SupportedDomains = ["maildrop.cc"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "maildrop.cc",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<MaildropAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@maildrop.cc"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["maildrop.cc"]));
        }
    }

    [TestCase(TestName = "Maildrop загружает inbox из публичного JSON endpoint"), Benchmark]
    public async Task MaildropRefreshesInboxFromPublicJsonEndpointTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/api/inbox/atom")
            {
                return JsonResponse(
                    "[" +
                    "{" +
                    "\"id\":\"md-1\"," +
                    "\"from\":\"sender@example.com\"," +
                    "\"subject\":\"Hello\"," +
                    "\"text\":\"Body text\"" +
                    "}" +
                    "]");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(MaildropProvider.DefaultApiUrl),
        };

        using var provider = new MaildropProvider(new MaildropProviderOptions
        {
            SupportedDomains = ["maildrop.cc"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "maildrop.cc",
        }, CancellationToken.None);

        var subjects = new List<string>();
        account.MailReceived += (_, args) => subjects.Add(args.Mail.Subject);

        var snapshot = (await account.RefreshInboxAsync()).ToArray();
        var mail = snapshot.Single();

        await mail.MarkAsReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Length.EqualTo(1));
            Assert.That(mail, Is.TypeOf<MaildropMail>());
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