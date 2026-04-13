using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class OneSecMailProviderTests(ILogger logger) : BenchmarkTests<OneSecMailProviderTests>(logger)
{
    public OneSecMailProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "1SecMail создаёт локальный аккаунт и кэширует домены"), Benchmark]
    public async Task OneSecMailCreatesLocalAccountAndCachesDomainsTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.Query == "?action=getDomainList")
            {
                return JsonResponse("[\"1secmail.com\",\"1secmail.org\"]");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(OneSecMailProvider.DefaultApiUrl),
        };

        using var provider = new OneSecMailProvider(httpClient);
        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "1secmail.com",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<OneSecMailAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@1secmail.com"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["1secmail.com", "1secmail.org"]));
        }
    }

    [TestCase(TestName = "1SecMail загружает inbox и поддерживает delete"), Benchmark]
    public async Task OneSecMailRefreshesInboxAndDeletesMailTest()
    {
        var deletedIds = new List<string>();

        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var query = request.RequestUri?.Query ?? string.Empty;

            if (query == "?action=getDomainList")
            {
                return JsonResponse("[\"1secmail.com\"]");
            }

            if (query == "?action=getMessages&login=atom&domain=1secmail.com")
            {
                return JsonResponse(
                    "[" +
                    "{" +
                    "\"id\":101," +
                    "\"from\":\"sender@example.com\"," +
                    "\"subject\":\"Hello\"" +
                    "}" +
                    "]");
            }

            if (query == "?action=readMessage&login=atom&domain=1secmail.com&id=101")
            {
                return JsonResponse(
                    "{" +
                    "\"id\":101," +
                    "\"from\":\"sender@example.com\"," +
                    "\"subject\":\"Hello\"," +
                    "\"textBody\":\"Body text\"" +
                    "}");
            }

            if (query == "?action=deleteMessage&login=atom&domain=1secmail.com&id=101")
            {
                deletedIds.Add("101");
                return JsonResponse("{" + "\"status\":true" + "}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(OneSecMailProvider.DefaultApiUrl),
        };

        using var provider = new OneSecMailProvider(httpClient);
        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "1secmail.com",
        }, CancellationToken.None);

        var subjects = new List<string>();
        account.MailReceived += (_, args) => subjects.Add(args.Mail.Subject);

        var snapshot = (await account.RefreshInboxAsync()).ToArray();
        var mail = snapshot.Single();

        await mail.MarkAsReadAsync();
        await mail.DeleteAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Length.EqualTo(1));
            Assert.That(mail, Is.TypeOf<OneSecMailMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello"]));
            Assert.That(deletedIds, Is.EqualTo(["101"]));
        }
    }

    private static HttpResponseMessage JsonResponse(string payload)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}