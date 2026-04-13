using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class TempailProviderTests(ILogger logger) : BenchmarkTests<TempailProviderTests>(logger)
{
    public TempailProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Tempail создаёт локальный mailbox и использует поддерживаемый домен"), Benchmark]
    public async Task TempailCreatesLocalMailboxAndUsesSupportedDomainTest()
    {
        using var provider = new TempailProvider(new TempailProviderOptions
        {
            SupportedDomains = ["necub.com"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "necub.com",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<TempailAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@necub.com"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["necub.com"]));
        }
    }

    [TestCase(TestName = "Tempail загружает inbox из публичной HTML страницы"), Benchmark]
    public async Task TempailRefreshesInboxFromPublicHtmlEndpointTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/en/inbox/atom%40necub.com")
            {
                return HtmlResponse(
                    "<html><body><table class=\"messages\">" +
                    "<tr data-id=\"tp-1\" data-from=\"sender@example.com\" data-subject=\"Hello from Tempail\">" +
                    "<td class=\"body\">Body text</td>" +
                    "</tr>" +
                    "</table></body></html>");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(TempailProvider.DefaultApiUrl),
        };

        using var provider = new TempailProvider(new TempailProviderOptions
        {
            SupportedDomains = ["necub.com"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "necub.com",
        }, CancellationToken.None);

        var subjects = new List<string>();
        account.MailReceived += (_, args) => subjects.Add(args.Mail.Subject);

        var snapshot = (await account.RefreshInboxAsync()).ToArray();
        var mail = snapshot.Single();

        await mail.MarkAsReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Length.EqualTo(1));
            Assert.That(mail, Is.TypeOf<TempailMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello from Tempail"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello from Tempail"]));
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