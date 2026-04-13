using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class FakeMailGeneratorProviderTests(ILogger logger) : BenchmarkTests<FakeMailGeneratorProviderTests>(logger)
{
    public FakeMailGeneratorProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Fake Mail Generator создаёт локальный mailbox и использует поддерживаемый домен"), Benchmark]
    public async Task FakeMailGeneratorCreatesLocalMailboxAndUsesSupportedDomainTest()
    {
        using var provider = new FakeMailGeneratorProvider(new FakeMailGeneratorProviderOptions
        {
            SupportedDomains = ["cuvox.de"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "cuvox.de",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<FakeMailGeneratorAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@cuvox.de"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["cuvox.de"]));
        }
    }

    [TestCase(TestName = "Fake Mail Generator загружает inbox из публичной HTML страницы"), Benchmark]
    public async Task FakeMailGeneratorRefreshesInboxFromPublicHtmlEndpointTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/inbox?email=atom%40cuvox.de")
            {
                return HtmlResponse(
                    "<html><body><ul class=\"messages\">" +
                    "<li data-id=\"fmg-1\" data-from=\"sender@example.com\" data-subject=\"Hello from FMG\">" +
                    "<p class=\"body\">Body text</p>" +
                    "</li>" +
                    "</ul></body></html>");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(FakeMailGeneratorProvider.DefaultApiUrl),
        };

        using var provider = new FakeMailGeneratorProvider(new FakeMailGeneratorProviderOptions
        {
            SupportedDomains = ["cuvox.de"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "cuvox.de",
        }, CancellationToken.None);

        var subjects = new List<string>();
        account.MailReceived += (_, args) => subjects.Add(args.Mail.Subject);

        var snapshot = (await account.RefreshInboxAsync()).ToArray();
        var mail = snapshot.Single();

        await mail.MarkAsReadAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Length.EqualTo(1));
            Assert.That(mail, Is.TypeOf<FakeMailGeneratorMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello from FMG"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello from FMG"]));
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