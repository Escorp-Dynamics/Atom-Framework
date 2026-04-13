using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

[Explicit]
[Category("Integration")]
[NonParallelizable]
public class LiveMailDeliveryProviderTests(ILogger logger) : BenchmarkTests<LiveMailDeliveryProviderTests>(logger)
{
    public LiveMailDeliveryProviderTests() : this(ConsoleLogger.Unicode) { }

    [TestCaseSource(nameof(GetProviderCases))]
    public async Task ProviderReceivesMailSentViaConfiguredSenderTest(string providerName, Func<ITemporaryEmailProvider> providerFactory)
    {
        if (!LiveMailDeliverySenderFactory.TryCreate(out var sender, out var reason))
        {
            Assert.Ignore(reason ?? "Конфигурация sender-а для live receive tests не задана.");
            return;
        }

        using var provider = providerFactory();
        using var account = await provider.CreateAccountAsync(TemporaryEmailAccountCreateSettings.Empty, CancellationToken.None);

        await account.ConnectAsync(CancellationToken.None);

        var token = $"atom-live-{Guid.NewGuid():N}";
        var subject = $"Atom live receive {token}";
        var body = $"provider={providerName}; token={token}";

        await sender!.SendAsync(account.Address, subject, body, CancellationToken.None);

        var received = await WaitForMailAsync(account, token, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(received, Is.Not.Null, $"Провайдер {providerName} не вернул письмо с тестовым токеном.");
            Assert.That(received!.Subject, Does.Contain(token));
            Assert.That(received.Body, Does.Contain(token));
        }
    }

    private static IEnumerable<TestCaseData> GetProviderCases()
    {
        yield return CreateCase("MailTm", static () => new MailTmProvider());
        yield return CreateCase("MailGw", static () => new MailGwProvider());
        yield return CreateCase("OneSecMail", static () => new OneSecMailProvider());
        yield return CreateCase("GuerrillaMail", static () => new GuerrillaMailProvider());
        yield return CreateCase("DropMail", static () => new DropMailProvider());
        yield return CreateCase("TempMailIo", static () => new TempMailIoProvider());
        yield return CreateCase("Emailnator", static () => new EmailnatorProvider());
        yield return CreateCase("EmailOnDeck", static () => new EmailOnDeckProvider());
        yield return CreateCase("Mailnesia", static () => new MailnesiaProvider());
        yield return CreateCase("TempMailOrg", static () => new TempMailOrgProvider());
        yield return CreateCase("Dispostable", static () => new DispostableProvider());
        yield return CreateCase("Mohmal", static () => new MohmalProvider());
        yield return CreateCase("Yopmail", static () => new YopmailProvider());
        yield return CreateCase("Maildrop", static () => new MaildropProvider());
        yield return CreateCase("MinuteInbox", static () => new MinuteInboxProvider());
        yield return CreateCase("Spambox", static () => new SpamboxProvider());
        yield return CreateCase("MailinatorPublic", static () => new MailinatorPublicProvider());
        yield return CreateCase("MailCatch", static () => new MailCatchProvider());
        yield return CreateCase("FakeMail", static () => new FakeMailProvider());
        yield return CreateCase("TempInbox", static () => new TempInboxProvider());
        yield return CreateCase("FakeMailGenerator", static () => new FakeMailGeneratorProvider());
        yield return CreateCase("GeneratorEmail", static () => new GeneratorEmailProvider());
        yield return CreateCase("Inboxes", static () => new InboxesProvider());
        yield return CreateCase("EmailFake", static () => new EmailFakeProvider());
        yield return CreateCase("Tempail", static () => new TempailProvider());
        yield return CreateCase("Moakt", static () => new MoaktProvider());
        yield return CreateCase("Mailgen", static () => new MailgenProvider());
        yield return CreateCase("DisposableMail", static () => new DisposableMailProvider());
    }

    private static TestCaseData CreateCase(string providerName, Func<ITemporaryEmailProvider> providerFactory)
        => new(providerName, providerFactory)
        {
            TestName = $"{providerName} получает письмо от configured test sender",
        };

    private static async ValueTask<Mail?> WaitForMailAsync(ITemporaryEmailAccount account, string token, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(2));

        while (!timeoutCts.IsCancellationRequested)
        {
            var snapshot = await account.RefreshInboxAsync(timeoutCts.Token).ConfigureAwait(false);
            var match = snapshot.FirstOrDefault(mail =>
                mail.Subject.Contains(token, StringComparison.Ordinal)
                || mail.Body.Contains(token, StringComparison.Ordinal));

            if (match is not null)
            {
                return match;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), timeoutCts.Token).ConfigureAwait(false);
        }

        return null;
    }
}