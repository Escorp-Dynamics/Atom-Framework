using System.Net;
using System.Text;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class GuerrillaMailProviderTests(ILogger logger) : BenchmarkTests<GuerrillaMailProviderTests>(logger)
{
    public GuerrillaMailProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "GuerrillaMail создаёт mailbox и использует поддерживаемые домены"), Benchmark]
    public async Task GuerrillaMailCreatesMailboxAndUsesSupportedDomainsTest()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.PathAndQuery == "/ajax.php?f=set_email_user&email_user=custom&domain=sharklasers.com")
            {
                return JsonResponse(
                    "{" +
                    "\"email_addr\":\"custom@sharklasers.com\"," +
                    "\"sid_token\":\"session-1\"" +
                    "}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(GuerrillaMailProvider.DefaultApiUrl),
        };

        using var provider = new GuerrillaMailProvider(new GuerrillaMailProviderOptions
        {
            SupportedDomains = ["sharklasers.com"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "Custom",
            Domain = "sharklasers.com",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account, Is.TypeOf<GuerrillaMailAccount>());
            Assert.That(account.Address, Is.EqualTo("custom@sharklasers.com"));
            Assert.That(provider.AvailableDomains, Is.EqualTo(["sharklasers.com"]));
        }
    }

    [TestCase(TestName = "GuerrillaMail загружает inbox и поддерживает delete"), Benchmark]
    public async Task GuerrillaMailRefreshesInboxAndDeletesMailTest()
    {
        var deletedIds = new List<string>();

        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            var pathAndQuery = request.RequestUri?.PathAndQuery ?? string.Empty;

            if (pathAndQuery == "/ajax.php?f=set_email_user&email_user=atom&domain=sharklasers.com")
            {
                return JsonResponse(
                    "{" +
                    "\"email_addr\":\"atom@sharklasers.com\"," +
                    "\"sid_token\":\"session-2\"" +
                    "}");
            }

            if (pathAndQuery == "/ajax.php?f=check_email&seq=0&sid_token=session-2")
            {
                return JsonResponse(
                    "{" +
                    "\"list\":[" +
                    "{" +
                    "\"mail_id\":\"501\"," +
                    "\"mail_from\":\"sender@example.com\"," +
                    "\"mail_subject\":\"Hello\"," +
                    "\"mail_excerpt\":\"Preview\"," +
                    "\"mail_read\":0" +
                    "}" +
                    "]" +
                    "}");
            }

            if (pathAndQuery == "/ajax.php?f=fetch_email&sid_token=session-2&email_id=501")
            {
                return JsonResponse(
                    "{" +
                    "\"mail_id\":\"501\"," +
                    "\"mail_from\":\"sender@example.com\"," +
                    "\"mail_subject\":\"Hello\"," +
                    "\"mail_body\":\"Body text\"" +
                    "}");
            }

            if (pathAndQuery == "/ajax.php?f=del_email&sid_token=session-2&email_ids%5B%5D=501")
            {
                deletedIds.Add("501");
                return JsonResponse("{" + "\"deleted\":true" + "}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }))
        {
            BaseAddress = new Uri(GuerrillaMailProvider.DefaultApiUrl),
        };

        using var provider = new GuerrillaMailProvider(new GuerrillaMailProviderOptions
        {
            SupportedDomains = ["sharklasers.com"],
        }, httpClient);

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
            Domain = "sharklasers.com",
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
            Assert.That(mail, Is.TypeOf<GuerrillaMailMail>());
            Assert.That(mail.Subject, Is.EqualTo("Hello"));
            Assert.That(mail.Body, Is.EqualTo("Body text"));
            Assert.That(mail.IsRead, Is.True);
            Assert.That(subjects, Is.EqualTo(["Hello"]));
            Assert.That(deletedIds, Is.EqualTo(["501"]));
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