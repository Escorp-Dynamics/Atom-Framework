using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class MoaktProviderTests(ILogger logger) : BenchmarkTests<MoaktProviderTests>(logger)
{
    public MoaktProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Moakt создаёт локальный mailbox и использует поддерживаемый домен"), Benchmark]
    public async Task MoaktCreatesLocalMailboxAndUsesSupportedDomainTest()
    {
        using var provider = new MoaktProvider(new MoaktProviderOptions
        {
            SupportedDomains = ["moakt.cc"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "moakt.cc",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<MoaktAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@moakt.cc"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["moakt.cc"]));
        }
    }

    [TestCase(TestName = "Moakt загружает inbox из публичной HTML страницы"), Benchmark]
    public async Task MoaktRefreshesInboxFromPublicHtmlEndpointTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/ru/inbox/atom%40moakt.cc")
            {
                return HtmlResponse(
                    "<html><body>" +
                    "<section data-id=\"moakt-1\" data-from=\"sender@example.com\" data-subject=\"Hello from Moakt\">" +
                    "<div class=\"body\">Body text</div>" +
                    "</section>" +
                    "</body></html>");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(MoaktProvider.DefaultApiUrl),
        };

        using var provider = new MoaktProvider(new MoaktProviderOptions
        {
            SupportedDomains = ["moakt.cc"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "moakt.cc",
        }, CancellationToken.None);

        var subjects = new List<string>();
        account.MailReceived += (_, args) => subjects.Add(args.Mail.Subject);

        var snapshot = (await account.RefreshInboxAsync()).ToArray();
        var mail = snapshot.Single();

        await mail.MarkAsReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Length.EqualTo(1));
            Assert.That(mail, Is.TypeOf<MoaktMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello from Moakt"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello from Moakt"]));
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