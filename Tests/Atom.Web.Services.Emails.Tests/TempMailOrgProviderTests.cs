using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class TempMailOrgProviderTests(ILogger logger) : BenchmarkTests<TempMailOrgProviderTests>(logger)
{
    public TempMailOrgProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "temp-mail.org создаёт локальный mailbox и использует поддерживаемые домены"), Benchmark]
    public async Task TempMailOrgCreatesLocalMailboxAndUsesSupportedDomainsTest()
    {
        using var provider = new TempMailOrgProvider(new TempMailOrgProviderOptions
        {
            SupportedDomains = ["temp-mail.org"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "temp-mail.org",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<TempMailOrgAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@temp-mail.org"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["temp-mail.org"]));
        }
    }

    [TestCase(TestName = "temp-mail.org загружает inbox и поддерживает delete"), Benchmark]
    public async Task TempMailOrgRefreshesInboxAndDeletesMailTest()
    {
        var deletedIds = new List<string>();

        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/request/mail/id/atom%40temp-mail.org")
            {
                return JsonResponse(
                    "[" +
                    "{" +
                    "\"id\":\"tm-1\"," +
                    "\"mail_from\":\"sender@example.com\"," +
                    "\"mail_subject\":\"Hello\"," +
                    "\"mail_text\":\"Body text\"" +
                    "}" +
                    "]");
            }

            if (request.Method == HttpMethod.Delete && request.RequestUri?.PathAndQuery == "/request/delete/id/atom%40temp-mail.org/tm-1")
            {
                deletedIds.Add("tm-1");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(TempMailOrgProvider.DefaultApiUrl),
        };

        using var provider = new TempMailOrgProvider(new TempMailOrgProviderOptions
        {
            SupportedDomains = ["temp-mail.org"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "temp-mail.org",
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
            Assert.That(mail, Is.TypeOf<TempMailOrgMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello"]));
            Assert.That(deletedIds, Is.EqualTo(["tm-1"]));
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