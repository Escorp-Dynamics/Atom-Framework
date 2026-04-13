using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class DisposableMailProviderTests(ILogger logger) : BenchmarkTests<DisposableMailProviderTests>(logger)
{
    public DisposableMailProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "DisposableMail создаёт локальный mailbox и использует поддерживаемый домен"), Benchmark]
    public async Task DisposableMailCreatesLocalMailboxAndUsesSupportedDomainTest()
    {
        using var provider = new DisposableMailProvider(new DisposableMailProviderOptions
        {
            SupportedDomains = ["disposablemail.com"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "disposablemail.com",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<DisposableMailAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@disposablemail.com"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["disposablemail.com"]));
        }
    }

    [TestCase(TestName = "DisposableMail загружает inbox из публичной HTML страницы"), Benchmark]
    public async Task DisposableMailRefreshesInboxFromPublicHtmlEndpointTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/atom%40disposablemail.com")
            {
                return HtmlResponse(
                    "<html><body><table class=\"messages\">" +
                    "<tr data-id=\"dm-1\" data-from=\"sender@example.com\" data-subject=\"Hello from DisposableMail\">" +
                    "<td class=\"body\">Body text</td>" +
                    "</tr>" +
                    "</table></body></html>");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(DisposableMailProvider.DefaultApiUrl),
        };

        using var provider = new DisposableMailProvider(new DisposableMailProviderOptions
        {
            SupportedDomains = ["disposablemail.com"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "disposablemail.com",
        }, CancellationToken.None);

        var subjects = new List<string>();
        account.MailReceived += (_, args) => subjects.Add(args.Mail.Subject);

        var snapshot = (await account.RefreshInboxAsync()).ToArray();
        var mail = snapshot.Single();

        await mail.MarkAsReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Length.EqualTo(1));
            Assert.That(mail, Is.TypeOf<DisposableMailMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello from DisposableMail"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello from DisposableMail"]));
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