using System.Net;
using System.Text;
using System.Text.Json;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class TempMailIoProviderTests(ILogger logger) : BenchmarkTests<TempMailIoProviderTests>(logger)
{
    public TempMailIoProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "temp-mail.io создаёт mailbox через JSON POST"), Benchmark]
    public async Task TempMailIoCreatesMailboxViaJsonPostTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.PathAndQuery == "/api/v3/email/new")
            {
                var body = await request.Content!.ReadAsStringAsync();
                using var document = JsonDocument.Parse(body);

                Assert.That(document.RootElement.GetProperty("name").GetString(), Is.EqualTo("custom"));
                Assert.That(document.RootElement.GetProperty("domain").GetString(), Is.EqualTo("temp-mail.io"));

                return JsonResponse("{" + "\"email\":\"custom@temp-mail.io\"" + "}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(TempMailIoProvider.DefaultApiUrl),
        };

        using var provider = new TempMailIoProvider(new TempMailIoProviderOptions
        {
            SupportedDomains = ["temp-mail.io"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "temp-mail.io",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<TempMailIoAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@temp-mail.io"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["temp-mail.io"]));
        }
    }

    [TestCase(TestName = "temp-mail.io загружает inbox и поддерживает delete"), Benchmark]
    public async Task TempMailIoRefreshesInboxAndDeletesMailTest()
    {
        var deletedIds = new List<string>();

        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.PathAndQuery == "/api/v3/email/new")
            {
                return Task.FromResult(JsonResponse("{" + "\"email\":\"atom@temp-mail.io\"" + "}"));
            }

            if (request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/api/v3/email/atom%40temp-mail.io/messages")
            {
                return Task.FromResult(JsonResponse(
                    "[" +
                    "{" +
                    "\"id\":\"m-101\"," +
                    "\"from\":\"sender@example.com\"," +
                    "\"to\":\"atom@temp-mail.io\"," +
                    "\"subject\":\"Hello\"," +
                    "\"body_text\":\"Body text\"," +
                    "\"seen\":false" +
                    "}" +
                    "]"));
            }

            if (request.Method == HttpMethod.Delete && request.RequestUri?.PathAndQuery == "/api/v3/email/atom%40temp-mail.io/messages/m-101")
            {
                deletedIds.Add("m-101");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }))
        {
            BaseAddress = new Uri(TempMailIoProvider.DefaultApiUrl),
        };

        using var provider = new TempMailIoProvider(new TempMailIoProviderOptions
        {
            SupportedDomains = ["temp-mail.io"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "temp-mail.io",
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
            Assert.That(mail, Is.TypeOf<TempMailIoMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello"]));
            Assert.That(deletedIds, Is.EqualTo(["m-101"]));
        }
    }

    private static HttpResponseMessage JsonResponse(string payload)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request);
    }
}