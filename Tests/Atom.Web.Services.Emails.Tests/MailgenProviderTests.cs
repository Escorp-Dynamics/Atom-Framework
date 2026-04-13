using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class MailgenProviderTests(ILogger logger) : BenchmarkTests<MailgenProviderTests>(logger)
{
    public MailgenProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Mailgen создаёт локальный mailbox и использует поддерживаемый домен"), Benchmark]
    public async Task MailgenCreatesLocalMailboxAndUsesSupportedDomainTest()
    {
        using var provider = new MailgenProvider(new MailgenProviderOptions
        {
            SupportedDomains = ["mailgen.biz"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "mailgen.biz",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<MailgenAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@mailgen.biz"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["mailgen.biz"]));
        }
    }

    [TestCase(TestName = "Mailgen загружает inbox из публичной HTML страницы"), Benchmark]
    public async Task MailgenRefreshesInboxFromPublicHtmlEndpointTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/atom%40mailgen.biz")
            {
                return HtmlResponse(
                    "<html><body><ul class=\"messages\">" +
                    "<li data-id=\"mg-1\" data-from=\"sender@example.com\" data-subject=\"Hello from Mailgen\">" +
                    "<div class=\"body\">Body text</div>" +
                    "</li>" +
                    "</ul></body></html>");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(MailgenProvider.DefaultApiUrl),
        };

        using var provider = new MailgenProvider(new MailgenProviderOptions
        {
            SupportedDomains = ["mailgen.biz"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "mailgen.biz",
        }, CancellationToken.None);

        var subjects = new List<string>();
        account.MailReceived += (_, args) => subjects.Add(args.Mail.Subject);

        var snapshot = (await account.RefreshInboxAsync()).ToArray();
        var mail = snapshot.Single();

        await mail.MarkAsReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Length.EqualTo(1));
            Assert.That(mail, Is.TypeOf<MailgenMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello from Mailgen"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello from Mailgen"]));
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