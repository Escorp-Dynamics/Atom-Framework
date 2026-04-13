using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class MailTmProviderTests(ILogger logger) : BenchmarkTests<MailTmProviderTests>(logger)
{
    public MailTmProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Mail.tm создаёт аккаунт и кэширует домены"), Benchmark]
    public async Task MailTmCreatesAccountAndCachesDomainsTest()
    {
        string? accountRequestBody = null;

        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/domains")
            {
                return JsonResponse("{" + "\"hydra:member\":[{" + "\"id\":\"dom-1\",\"domain\":\"mail.tm\",\"isActive\":true,\"isPrivate\":false" + "}]}");
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/accounts")
            {
                accountRequestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonLdResponse("{" + "\"id\":\"account-1\",\"address\":\"custom@mail.tm\"" + "}");
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/token")
            {
                return JsonResponse("{" + "\"token\":\"jwt-token-1\"" + "}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(MailTmProvider.DefaultApiUrl),
        };

        using var provider = new MailTmProvider(httpClient);
        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "mail.tm",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<MailTmAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@mail.tm"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["mail.tm"]));
            Assert.That(accountRequestBody, Is.Not.Null);
            Assert.That(accountRequestBody, Does.Contain("custom@mail.tm"));
        }
    }

    [TestCase(TestName = "Mail.tm загружает inbox и поддерживает mark-as-read plus delete"), Benchmark]
    public async Task MailTmRefreshesInboxAndExecutesMailOperationsTest()
    {
        var patchedIds = new List<string>();
        var deletedIds = new List<string>();

        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/domains")
            {
                return JsonResponse("{" + "\"hydra:member\":[{" + "\"id\":\"dom-1\",\"domain\":\"mail.tm\",\"isActive\":true,\"isPrivate\":false" + "}]}");
            }

            if (request.Method == HttpMethod.Post && path == "/accounts")
            {
                return JsonLdResponse("{" + "\"id\":\"account-1\",\"address\":\"atom@mail.tm\"" + "}");
            }

            if (request.Method == HttpMethod.Post && path == "/token")
            {
                return JsonResponse("{" + "\"token\":\"jwt-token-1\"" + "}");
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
                    "\"to\":[{\"address\":\"atom@mail.tm\"}]" +
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
                  "\"to\":[{\"address\":\"atom@mail.tm\"}]" +
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
            BaseAddress = new Uri(MailTmProvider.DefaultApiUrl),
        };

        using var provider = new MailTmProvider(httpClient);
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
            Assert.That(mail, Is.TypeOf<MailTmMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(subjects, Is.EqualTo(["Hello"]));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(patchedIds, Is.EqualTo(["message-1"]));
            Assert.That(deletedIds, Is.EqualTo(["message-1"]));
        }
    }

    [TestCase(TestName = "Mail.tm options управляют base URL, refresh mode и генерацией учётных данных"), Benchmark]
    public async Task MailTmOptionsControlBaseUrlRefreshAndCredentialGenerationTest()
    {
        var domainRequests = 0;
        var accountBodies = new List<string>();
        var requestHosts = new List<string>();

        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestHosts.Add(request.RequestUri?.Host ?? string.Empty);

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/domains")
            {
                domainRequests++;
                return JsonResponse("{" + "\"hydra:member\":[{" + "\"id\":\"dom-1\",\"domain\":\"mail.tm\",\"isActive\":true,\"isPrivate\":false" + "}]}");
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/accounts")
            {
                accountBodies.Add(request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty);
                var index = accountBodies.Count;
                return JsonLdResponse("{" + "\"id\":\"account-" + index + "\",\"address\":\"lababcd@mail.tm\"" + "}");
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/token")
            {
                return JsonResponse("{" + "\"token\":\"jwt-token-options\"" + "}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));

        using var provider = new MailTmProvider(new MailTmProviderOptions
        {
            BaseUrl = "https://mail.tm.custom/",
            DomainRefreshMode = TemporaryEmailDomainRefreshMode.Always,
            GeneratedAliasPrefix = "lab",
            GeneratedAliasRandomLength = 4,
            GeneratedPasswordRandomLength = 6,
            GeneratedPasswordSuffix = "Z9!",
        }, httpClient);

        _ = await provider.CreateAccountAsync(TemporaryEmailAccountCreateSettings.Empty, CancellationToken.None);
        _ = await provider.CreateAccountAsync(TemporaryEmailAccountCreateSettings.Empty, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(domainRequests, Is.EqualTo(2));
            Assert.That(requestHosts, Is.All.EqualTo("mail.tm.custom"));
            Assert.That(accountBodies, Has.Count.EqualTo(2));
            Assert.That(accountBodies[0], Does.Match("\"address\":\"lab[a-f0-9]{4}@mail\\.tm\""));
            Assert.That(accountBodies[0], Does.Match("\"password\":\"[a-f0-9]{6}Z9!\""));
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