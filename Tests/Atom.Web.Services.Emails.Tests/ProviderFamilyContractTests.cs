using Atom.Web.Emails;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class ProviderFamilyContractTests(ILogger logger) : BenchmarkTests<ProviderFamilyContractTests>(logger)
{
    public ProviderFamilyContractTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Fixed-domain provider сортирует и дедуплицирует домены"), Benchmark]
    public async Task FixedDomainProviderSortsAndDeduplicatesDomainsTest()
    {
        using var provider = new TestFixedDomainProvider(new TestFixedDomainOptions
        {
            BaseUrl = "https://example.test/",
            SupportedDomains = ["b.test", "a.test", "A.test", "b.test"],
        });

        var account = await provider.CreateAccountAsync(new TemporaryEmailAccountCreateSettings
        {
            Alias = "atom",
        }, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.AvailableDomains, Is.EqualTo(["a.test", "b.test"]));
            Assert.That(account.Address, Is.EqualTo("atom@a.test"));
        }
    }

    [TestCase(TestName = "Session account base даёт общий lifecycle и unsupported send"), Benchmark]
    public async Task SessionTemporaryEmailAccountProvidesSharedLifecycleTest()
    {
        using var provider = new TestProvider();
        using var account = new TestSessionAccount(provider, Guid.NewGuid(), "atom@example.test", "session-1");

        await account.ConnectAsync(CancellationToken.None);
        await account.DisconnectAsync(CancellationToken.None);

        var helperException = Assert.Throws<NotSupportedException>(() => account.EnsureCanSend());
        var exception = Assert.ThrowsAsync<NotSupportedException>(async () => await account.SendAsync(new Mail("from", "to", "subject", "body"), CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account.ExposedSessionKey, Is.EqualTo("session-1"));
            Assert.That(account.Provider, Is.SameAs(provider));
            Assert.That(account.CanSend, Is.False);
            Assert.That(helperException?.Message, Is.EqualTo("Почтовый аккаунт 'atom@example.test' не поддерживает исходящую отправку."));
            Assert.That(exception?.Message, Is.EqualTo("session account send not supported"));
        }
    }

    [TestCase(TestName = "Provider-bound account base даёт общий lifecycle и unsupported send"), Benchmark]
    public async Task ProviderTemporaryEmailAccountProvidesSharedLifecycleTest()
    {
        using var provider = new TestProvider();
        using var account = new TestProviderBoundAccount(provider, Guid.NewGuid(), "atom@example.test");

        await account.ConnectAsync(CancellationToken.None);
        await account.DisconnectAsync(CancellationToken.None);

        var helperException = Assert.Throws<NotSupportedException>(() => account.EnsureCanSend());
        var exception = Assert.ThrowsAsync<NotSupportedException>(async () => await account.SendAsync(new Mail("from", "to", "subject", "body"), CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account.Provider, Is.SameAs(provider));
            Assert.That(account.CanSend, Is.False);
            Assert.That(helperException?.Message, Is.EqualTo("Почтовый аккаунт 'atom@example.test' не поддерживает исходящую отправку."));
            Assert.That(exception?.Message, Is.EqualTo("provider account send not supported"));
        }
    }

    private sealed class TestFixedDomainOptions : FixedDomainTemporaryEmailProviderOptions;

    private sealed class TestFixedDomainProvider(TestFixedDomainOptions options) : FixedDomainTemporaryEmailProvider<TestFixedDomainOptions>("TestProvider", options)
    {
        protected override async ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(TemporaryEmailAccountCreateSettings request, CancellationToken cancellationToken)
        {
            var address = await ResolveAddressAsync(request, cancellationToken).ConfigureAwait(false);
            return new TestProviderBoundAccount(new TestProvider(), Guid.NewGuid(), address);
        }
    }

    private sealed class TestProvider : TemporaryEmailProvider
    {
        protected override ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(TemporaryEmailAccountCreateSettings request, CancellationToken cancellationToken)
            => ValueTask.FromException<ITemporaryEmailAccount>(new NotSupportedException());
    }

    private sealed class TestSessionAccount(TestProvider provider, Guid id, string address, string sessionKey)
        : SessionTemporaryEmailAccount<TestProvider>(provider, id, address, sessionKey, "session account send not supported")
    {
        public string ExposedSessionKey => SessionKey;

        protected override ValueTask<IEnumerable<Mail>> LoadMessagesAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult<IEnumerable<Mail>>([]);
    }

    private sealed class TestProviderBoundAccount(TestProvider provider, Guid id, string address)
        : ProviderTemporaryEmailAccount<TestProvider>(provider, id, address, "provider account send not supported")
    {
        protected override ValueTask<IEnumerable<Mail>> LoadMessagesAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult<IEnumerable<Mail>>([]);
    }
}