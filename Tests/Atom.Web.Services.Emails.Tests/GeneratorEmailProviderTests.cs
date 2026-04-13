using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class GeneratorEmailProviderTests(ILogger logger) : BenchmarkTests<GeneratorEmailProviderTests>(logger)
{
    public GeneratorEmailProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "generator.email создаёт локальный mailbox и использует поддерживаемый домен"), Benchmark]
    public async Task GeneratorEmailCreatesLocalMailboxAndUsesSupportedDomainTest()
    {
        using var provider = new GeneratorEmailProvider(new GeneratorEmailProviderOptions
        {
            SupportedDomains = ["mail-temp.com"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "mail-temp.com",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<GeneratorEmailAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@mail-temp.com"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["mail-temp.com"]));
        }
    }

    [TestCase(TestName = "generator.email загружает inbox из публичной HTML страницы"), Benchmark]
    public async Task GeneratorEmailRefreshesInboxFromPublicHtmlEndpointTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/atom%40mail-temp.com")
            {
                return HtmlResponse(
                    "<html><body><ul class=\"messages\">" +
                    "<li data-id=\"gen-1\" data-from=\"sender@example.com\" data-subject=\"Hello from generator.email\">" +
                    "<div class=\"body\">Body text</div>" +
                    "</li>" +
                    "</ul></body></html>");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(GeneratorEmailProvider.DefaultApiUrl),
        };

        using var provider = new GeneratorEmailProvider(new GeneratorEmailProviderOptions
        {
            SupportedDomains = ["mail-temp.com"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "mail-temp.com",
        }, CancellationToken.None);

        var subjects = new List<string>();
        account.MailReceived += (_, args) => subjects.Add(args.Mail.Subject);

        var snapshot = (await account.RefreshInboxAsync()).ToArray();
        var mail = snapshot.Single();

        await mail.MarkAsReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Length.EqualTo(1));
            Assert.That(mail, Is.TypeOf<GeneratorEmailMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello from generator.email"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello from generator.email"]));
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