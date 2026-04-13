using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class MailGwProviderTests(ILogger logger) : BenchmarkTests<MailGwProviderTests>(logger)
{
    public MailGwProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Mail.gw создаёт аккаунт и кэширует домены"), Benchmark]
    public async Task MailGwCreatesAccountAndCachesDomainsTest()
    {
        string? accountRequestBody = null;

        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/domains")
            {
                return JsonResponse("{" + "\"hydra:member\":[{" + "\"id\":\"dom-1\",\"domain\":\"mail.gw\",\"isActive\":true,\"isPrivate\":false" + "}]}");
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/accounts")
            {
                accountRequestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonLdResponse("{" + "\"id\":\"account-1\",\"address\":\"custom@mail.gw\"" + "}");
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/token")
            {
                return JsonResponse("{" + "\"token\":\"jwt-token-gw-1\"" + "}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(MailGwProvider.DefaultApiUrl),
        };

        using var provider = new MailGwProvider(httpClient);
        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "mail.gw",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<MailGwAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@mail.gw"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["mail.gw"]));
            Assert.That(accountRequestBody, Is.Not.Null);
            Assert.That(accountRequestBody, Does.Contain("custom@mail.gw"));
        }
    }

    [TestCase(TestName = "Mail.gw загружает inbox и поддерживает mark-as-read plus delete"), Benchmark]
    public async Task MailGwRefreshesInboxAndExecutesMailOperationsTest()
    {
        var patchedIds = new List<string>();
        var deletedIds = new List<string>();

        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/domains")
            {
                return JsonResponse("{" + "\"hydra:member\":[{" + "\"id\":\"dom-1\",\"domain\":\"mail.gw\",\"isActive\":true,\"isPrivate\":false" + "}]}");
            }

            if (request.Method == HttpMethod.Post && path == "/accounts")
            {
                return JsonLdResponse("{" + "\"id\":\"account-1\",\"address\":\"atom@mail.gw\"" + "}");
            }

            if (request.Method == HttpMethod.Post && path == "/token")
            {
                return JsonResponse("{" + "\"token\":\"jwt-token-gw-1\"" + "}");
            }

            if (request.Method == HttpMethod.Get && path == "/messages")
            {
                return JsonLdResponse(
                    "{" +
                    "\"hydra:member\":[{" +
                    "\"id\":\"message-1\"," +
                    "\"subject\":\"Hello\"," +
                    "\"intro\":\"Preview\"," +
                    "\"seen\":false," +
                    "\"from\":{\"address\":\"sender@example.com\"}," +
                    "\"to\":[{\"address\":\"atom@mail.gw\"}]" +
                    "}]}"
                );
            }

            if (request.Method == HttpMethod.Get && path == "/messages/message-1")
            {
                return JsonLdResponse(
                  "{" +
                  "\"id\":\"message-1\"," +
                  "\"subject\":\"Hello\"," +
                  "\"text\":\"Body text\"," +
                  "\"intro\":\"Preview\"," +
                  "\"seen\":false," +
                  "\"from\":{\"address\":\"sender@example.com\"}," +
                  "\"to\":[{\"address\":\"atom@mail.gw\"}]" +
                  "}"
                );
            }

            if (request.Method.Method == "PATCH" && path == "/messages/message-1")
            {
                patchedIds.Add("message-1");
                return JsonLdResponse("{" + "\"seen\":true" + "}");
            }

            if (request.Method == HttpMethod.Delete && path == "/messages/message-1")
            {
                deletedIds.Add("message-1");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(MailGwProvider.DefaultApiUrl),
        };

        using var provider = new MailGwProvider(httpClient);
        var account = await provider.CreateAccountAsync(TemporaryEmailAccountCreateSettings.Empty, CancellationToken.None);

        var subjects = new List<string>();
        account.MailReceived += (_, args) => subjects.Add(args.Mail.Subject);

        var snapshot = (await account.RefreshInboxAsync()).ToArray();
        var mail = snapshot.Single();

        await mail.MarkAsReadAsync();
        await mail.DeleteAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Length.EqualTo(1));
            Assert.That(mail, Is.TypeOf<MailGwMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(subjects, Is.EqualTo(["Hello"]));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(patchedIds, Is.EqualTo(["message-1"]));
            Assert.That(deletedIds, Is.EqualTo(["message-1"]));
        }
    }

    private static HttpResponseMessage JsonResponse(string payload)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage JsonLdResponse(string payload)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/ld+json"),
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}