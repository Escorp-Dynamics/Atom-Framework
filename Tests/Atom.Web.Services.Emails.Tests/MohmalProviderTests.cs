using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class MohmalProviderTests(ILogger logger) : BenchmarkTests<MohmalProviderTests>(logger)
{
    public MohmalProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Mohmal создаёт локальный mailbox и использует поддерживаемый домен"), Benchmark]
    public async Task MohmalCreatesLocalMailboxAndUsesSupportedDomainTest()
    {
        using var provider = new MohmalProvider(new MohmalProviderOptions
        {
            SupportedDomains = ["mohmal.com"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "mohmal.com",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<MohmalAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@mohmal.com"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["mohmal.com"]));
        }
    }

    [TestCase(TestName = "Mohmal загружает inbox из HTML страницы"), Benchmark]
    public async Task MohmalRefreshesInboxFromHtmlPageTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/en/inbox/atom")
            {
                return HtmlResponse(
                    "<html><body>" +
                    "<article data-id=\"mh-1\" data-from=\"sender@example.com\" data-subject=\"Hello\">" +
                    "<div class=\"body\">Body text</div>" +
                    "</article>" +
                    "</body></html>");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(MohmalProvider.DefaultApiUrl),
        };

        using var provider = new MohmalProvider(new MohmalProviderOptions
        {
            SupportedDomains = ["mohmal.com"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "mohmal.com",
        }, CancellationToken.None);

        var subjects = new List<string>();
        account.MailReceived += (_, args) => subjects.Add(args.Mail.Subject);

        var snapshot = (await account.RefreshInboxAsync()).ToArray();
        var mail = snapshot.Single();

        await mail.MarkAsReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Length.EqualTo(1));
            Assert.That(mail, Is.TypeOf<MohmalMail>());
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