using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class YopmailProviderTests(ILogger logger) : BenchmarkTests<YopmailProviderTests>(logger)
{
    public YopmailProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Yopmail создаёт локальный mailbox и использует поддерживаемый домен"), Benchmark]
    public async Task YopmailCreatesLocalMailboxAndUsesSupportedDomainTest()
    {
        using var provider = new YopmailProvider(new YopmailProviderOptions
        {
            SupportedDomains = ["yopmail.com"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "yopmail.com",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<YopmailAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@yopmail.com"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["yopmail.com"]));
        }
    }

    [TestCase(TestName = "Yopmail загружает inbox из HTML страницы"), Benchmark]
    public async Task YopmailRefreshesInboxFromHtmlPageTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/en/inbox?login=atom")
            {
                return HtmlResponse(
                    "<html><body><ul>" +
                    "<li data-id=\"yo-1\" data-from=\"sender@example.com\" data-subject=\"Hello\">" +
                    "<p class=\"body\">Body text</p>" +
                    "</li>" +
                    "</ul></body></html>");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(YopmailProvider.DefaultApiUrl),
        };

        using var provider = new YopmailProvider(new YopmailProviderOptions
        {
            SupportedDomains = ["yopmail.com"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "yopmail.com",
        }, CancellationToken.None);

        var subjects = new List<string>();
        account.MailReceived += (_, args) => subjects.Add(args.Mail.Subject);

        var snapshot = (await account.RefreshInboxAsync()).ToArray();
        var mail = snapshot.Single();

        await mail.MarkAsReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Length.EqualTo(1));
            Assert.That(mail, Is.TypeOf<YopmailMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello"]));
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