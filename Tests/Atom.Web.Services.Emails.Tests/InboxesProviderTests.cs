using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class InboxesProviderTests(ILogger logger) : BenchmarkTests<InboxesProviderTests>(logger)
{
    public InboxesProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "inboxes.com создаёт локальный mailbox и использует поддерживаемый домен"), Benchmark]
    public async Task InboxesCreatesLocalMailboxAndUsesSupportedDomainTest()
    {
        using var provider = new InboxesProvider(new InboxesProviderOptions
        {
            SupportedDomains = ["inboxes.com"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "inboxes.com",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<InboxesAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@inboxes.com"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["inboxes.com"]));
        }
    }

    [TestCase(TestName = "inboxes.com загружает inbox из публичной HTML страницы"), Benchmark]
    public async Task InboxesRefreshesInboxFromPublicHtmlEndpointTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/atom%40inboxes.com")
            {
                return HtmlResponse(
                    "<html><body><table class=\"messages\">" +
                    "<tr data-id=\"ibox-1\" data-from=\"sender@example.com\" data-subject=\"Hello from Inboxes\">" +
                    "<td class=\"body\">Body text</td>" +
                    "</tr>" +
                    "</table></body></html>");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(InboxesProvider.DefaultApiUrl),
        };

        using var provider = new InboxesProvider(new InboxesProviderOptions
        {
            SupportedDomains = ["inboxes.com"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "inboxes.com",
        }, CancellationToken.None);

        var subjects = new List<string>();
        account.MailReceived += (_, args) => subjects.Add(args.Mail.Subject);

        var snapshot = (await account.RefreshInboxAsync()).ToArray();
        var mail = snapshot.Single();

        await mail.MarkAsReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Length.EqualTo(1));
            Assert.That(mail, Is.TypeOf<InboxesMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello from Inboxes"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello from Inboxes"]));
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