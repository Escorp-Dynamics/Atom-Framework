using System.Net;
using System.Text;
using System.Text.Json;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class DropMailProviderTests(ILogger logger) : BenchmarkTests<DropMailProviderTests>(logger)
{
    public DropMailProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "DropMail создаёт mailbox через GraphQL mutation"), Benchmark]
    public async Task DropMailCreatesMailboxViaGraphQlMutationTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));

            var body = await request.Content!.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            Assert.That(root.GetProperty("query").GetString(), Does.Contain("introduceSession"));
            Assert.That(root.GetProperty("variables").GetProperty("address").GetString(), Is.EqualTo("custom@dropmail.me"));

            return JsonResponse(
                "{" +
                "\"data\":{" +
                "\"introduceSession\":{" +
                "\"id\":\"session-1\"," +
                "\"addresses\":[{" +
                "\"address\":\"custom@dropmail.me\"" +
                "}]" +
                "}" +
                "}" +
                "}");
        }))
        {
            BaseAddress = new Uri(DropMailProvider.DefaultApiUrl),
        };

        using var provider = new DropMailProvider(new DropMailProviderOptions
        {
            SupportedDomains = ["dropmail.me"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "dropmail.me",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<DropMailAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@dropmail.me"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["dropmail.me"]));
        }
    }

    [TestCase(TestName = "DropMail загружает inbox через GraphQL session query"), Benchmark]
    public async Task DropMailRefreshesInboxViaGraphQlSessionQueryTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var query = root.GetProperty("query").GetString() ?? string.Empty;

            if (query.Contains("introduceSession", StringComparison.Ordinal))
            {
                return JsonResponse(
                    "{" +
                    "\"data\":{" +
                    "\"introduceSession\":{" +
                    "\"id\":\"session-2\"," +
                    "\"addresses\":[{" +
                    "\"address\":\"atom@dropmail.me\"" +
                    "}]" +
                    "}" +
                    "}" +
                    "}");
            }

            if (query.Contains("session", StringComparison.Ordinal))
            {
                Assert.That(root.GetProperty("variables").GetProperty("id").GetString(), Is.EqualTo("session-2"));

                return JsonResponse(
                    "{" +
                    "\"data\":{" +
                    "\"session\":{" +
                    "\"id\":\"session-2\"," +
                    "\"mails\":[{" +
                    "\"id\":\"101\"," +
                    "\"fromAddr\":\"sender@example.com\"," +
                    "\"toAddr\":\"atom@dropmail.me\"," +
                    "\"headerSubject\":\"Hello\"," +
                    "\"text\":\"Body text\"," +
                    "\"seen\":false" +
                    "}]" +
                    "}" +
                    "}" +
                    "}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(DropMailProvider.DefaultApiUrl),
        };

        using var provider = new DropMailProvider(new DropMailProviderOptions
        {
            SupportedDomains = ["dropmail.me"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "dropmail.me",
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
            Assert.That(mail, Is.TypeOf<DropMailMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello"]));
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