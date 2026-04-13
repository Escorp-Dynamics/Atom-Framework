using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class EmailFakeProviderTests(ILogger logger) : BenchmarkTests<EmailFakeProviderTests>(logger)
{
    public EmailFakeProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "emailfake.com создаёт локальный mailbox и использует поддерживаемый домен"), Benchmark]
    public async Task EmailFakeCreatesLocalMailboxAndUsesSupportedDomainTest()
    {
        using var provider = new EmailFakeProvider(new EmailFakeProviderOptions
        {
            SupportedDomains = ["adsensekorea.com"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "adsensekorea.com",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<EmailFakeAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@adsensekorea.com"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["adsensekorea.com"]));
        }
    }

    [TestCase(TestName = "emailfake.com загружает inbox из публичной HTML страницы"), Benchmark]
    public async Task EmailFakeRefreshesInboxFromPublicHtmlEndpointTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/adsensekorea.com/atom")
            {
                return HtmlResponse(
                    "<html><body>" +
                    "<article data-id=\"ef-1\" data-from=\"sender@example.com\" data-subject=\"Hello from emailfake.com\">" +
                    "<div class=\"body\">Body text</div>" +
                    "</article>" +
                    "</body></html>");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(EmailFakeProvider.DefaultApiUrl),
        };

        using var provider = new EmailFakeProvider(new EmailFakeProviderOptions
        {
            SupportedDomains = ["adsensekorea.com"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "adsensekorea.com",
        }, CancellationToken.None);

        var subjects = new List<string>();
        account.MailReceived += (_, args) => subjects.Add(args.Mail.Subject);

        var snapshot = (await account.RefreshInboxAsync()).ToArray();
        var mail = snapshot.Single();

        await mail.MarkAsReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Length.EqualTo(1));
            Assert.That(mail, Is.TypeOf<EmailFakeMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello from emailfake.com"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello from emailfake.com"]));
        }
    }

    private static HttpResponseMessage HtmlResponse(string payload)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "text/html"),
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}