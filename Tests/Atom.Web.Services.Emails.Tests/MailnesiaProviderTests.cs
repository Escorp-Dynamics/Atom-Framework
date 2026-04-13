using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class MailnesiaProviderTests(ILogger logger) : BenchmarkTests<MailnesiaProviderTests>(logger)
{
    public MailnesiaProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Mailnesia создаёт локальный mailbox и использует поддерживаемые домены"), Benchmark]
    public async Task MailnesiaCreatesLocalMailboxAndUsesSupportedDomainsTest()
    {
        using var provider = new MailnesiaProvider(new MailnesiaProviderOptions
        {
            SupportedDomains = ["mailnesia.com"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "mailnesia.com",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<MailnesiaAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@mailnesia.com"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["mailnesia.com"]));
        }
    }

    [TestCase(TestName = "Mailnesia загружает inbox из XML feed"), Benchmark]
    public async Task MailnesiaRefreshesInboxFromXmlFeedTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/mailbox/atom.xml")
            {
                return XmlResponse(
                    "<feed>" +
                    "<entry>" +
                    "<id>701</id>" +
                    "<from>sender@example.com</from>" +
                    "<title>Hello</title>" +
                    "<content>Body text</content>" +
                    "</entry>" +
                    "</feed>");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(MailnesiaProvider.DefaultApiUrl),
        };

        using var provider = new MailnesiaProvider(new MailnesiaProviderOptions
        {
            SupportedDomains = ["mailnesia.com"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "mailnesia.com",
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
            Assert.That(mail, Is.TypeOf<MailnesiaMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello"]));
        }
    }

    private static HttpResponseMessage XmlResponse(string payload)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/xml"),
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}