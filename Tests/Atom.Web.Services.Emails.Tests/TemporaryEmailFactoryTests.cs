using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class TemporaryEmailFactoryTests(ILogger logger) : BenchmarkTests<TemporaryEmailFactoryTests>(logger)
{
    public TemporaryEmailFactoryTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "Фабрика создаёт аккаунт и привязывает его к себе"), Benchmark]
    public async Task FactoryCreatesAccountAndAttachesItselfAsOwnerTest()
    {
        using var factory = new TemporaryEmailFactory();
        using var provider = new FakeTemporaryEmailProvider(
            ["mail-a.test"],
            () => new FakeTemporaryEmailAccount(
                id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                address: "box@mail-a.test",
                [
                    [new Mail(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "sender@mail.test", "box@mail-a.test", "hello", "body-1")],
                ]));
        factory.Use(provider);

        var account = await factory.GetAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account.Provider, Is.SameAs(provider));
            Assert.That(account.Address, Is.EqualTo("box@mail-a.test"));
            Assert.That(factory.Count, Is.EqualTo(1));
            Assert.That(factory.AvailableDomains, Is.EqualTo(["mail-a.test"]));
        }

        factory.Return(account);
    }

    [TestCase(TestName = "Фабрика чередует провайдеров по round-robin"), Benchmark]
    public async Task FactoryRotatesProvidersRoundRobinTest()
    {
        using var factory = new TemporaryEmailFactory();
        factory.Use(new FakeTemporaryEmailProvider(
            ["mail-a.test"],
            () => new FakeTemporaryEmailAccount(Guid.Parse("11111111-1111-1111-1111-111111111111"), "one@mail-a.test", [])));
        factory.Use(new FakeTemporaryEmailProvider(
            ["mail-b.test"],
            () => new FakeTemporaryEmailAccount(Guid.Parse("22222222-2222-2222-2222-222222222222"), "two@mail-b.test", [])));

        var first = await factory.GetAsync();
        var second = await factory.GetAsync();
        var third = await factory.GetAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Address, Is.EqualTo("one@mail-a.test"));
            Assert.That(second.Address, Is.EqualTo("two@mail-b.test"));
            Assert.That(third.Address, Is.EqualTo("one@mail-a.test"));
        }

        factory.Return(first);
        factory.Return(second);
        factory.Return(third);
    }

    [TestCase(TestName = "Фабрика учитывает желаемый домен при выборе провайдера"), Benchmark]
    public async Task FactoryChoosesProviderByRequestedDomainTest()
    {
        using var factory = new TemporaryEmailFactory();
        factory.Use(new FakeTemporaryEmailProvider(
            ["mail-a.test"],
            () => new FakeTemporaryEmailAccount(Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"), "one@mail-a.test", [])));
        factory.Use(new FakeTemporaryEmailProvider(
            ["mail-b.test"],
            () => new FakeTemporaryEmailAccount(Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222"), "two@mail-b.test", [])));

        var account = await factory.GetAsync(new TemporaryEmailAccountCreateSettings
        {
            Domain = "mail-b.test",
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account.Address, Is.EqualTo("two@mail-b.test"));
            Assert.That(account.Domain, Is.EqualTo("mail-b.test"));
        }

        factory.Return(account);
    }

    [TestCase(TestName = "Фабрика возвращает аккаунт и освобождает его lifecycle"), Benchmark]
    public async Task FactoryReturnDisposesAccountTest()
    {
        using var factory = new TemporaryEmailFactory();
        using var provider = new FakeTemporaryEmailProvider(
            ["mail-a.test"],
            () => new FakeTemporaryEmailAccount(Guid.Parse("11111111-1111-1111-1111-111111111111"), "one@mail-a.test", []));
        factory.Use(provider);

        var account = (FakeTemporaryEmailAccount)await factory.GetAsync();

        factory.Return(account);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.Count, Is.Zero);
            Assert.That(account.IsDisposed, Is.True);
        }
    }

    [TestCase(TestName = "Аккаунт поднимает событие только для новых писем"), Benchmark]
    public async Task AccountRaisesEventOnlyForNewMessagesTest()
    {
        using var account = new FakeTemporaryEmailAccount(
            id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            address: "one@mail-a.test",
            [
                [new Mail(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "sender-1@mail.test", "one@mail-a.test", "first", "body-1")],
                [
                    new Mail(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "sender-1@mail.test", "one@mail-a.test", "first", "body-1"),
                    new Mail(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "sender-2@mail.test", "one@mail-a.test", "second", "body-2"),
                ],
            ]);

        var receivedSubjects = new List<string>();
        account.MailReceived += (_, e) =>
            receivedSubjects.Add(e.Mail.Subject);

        var firstSnapshot = await account.RefreshInboxAsync();
        var secondSnapshot = await account.RefreshInboxAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstSnapshot.Select(static email => email.Subject), Is.EqualTo(["first"]));
            Assert.That(secondSnapshot.Select(static email => email.Subject), Is.EqualTo(["first", "second"]));
            Assert.That(receivedSubjects, Is.EqualTo(["first", "second"]));
            Assert.That(account.Inbox.Select(static mail => mail.Subject), Is.EqualTo(["first", "second"]));
            Assert.That(account.Count, Is.EqualTo(2));
        }
    }

    [TestCase(TestName = "Базовый Mail поддерживает отметку прочитанным и удаление"), Benchmark]
    public async Task MailSupportsReadAndDeleteOperationsTest()
    {
        var mail = new Mail(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "sender@mail.test", "target@mail.test", "subject", "body");

        await mail.MarkAsReadAsync();
        await mail.DeleteAsync();

        Assert.That(mail.IsRead, Is.True);
    }

    private sealed class FakeTemporaryEmailProvider(
        IEnumerable<string> domains,
        Func<FakeTemporaryEmailAccount> accountFactory) : TemporaryEmailProvider
    {
        public override IEnumerable<string> AvailableDomains { get; } = [.. domains];

        protected override ValueTask<ITemporaryEmailAccount> CreateAccountCoreAsync(
            TemporaryEmailAccountCreateSettings request,
            CancellationToken cancellationToken)
            => ValueTask.FromResult<ITemporaryEmailAccount>(accountFactory());
    }

    private sealed class FakeTemporaryEmailAccount(
        Guid id,
        string address,
        params IEnumerable<Mail>[] inboxSnapshots) : TemporaryEmailAccount(id, address)
    {
        private readonly Queue<Mail[]> snapshots = new(inboxSnapshots.Select(static snapshot => snapshot.ToArray()));

        public bool IsDisposed { get; private set; }

        protected override ValueTask<IEnumerable<Mail>> LoadMessagesAsync(CancellationToken cancellationToken)
        {
            if (snapshots.Count == 0)
            {
                return ValueTask.FromResult<IEnumerable<Mail>>([]);
            }

            var snapshot = snapshots.Count == 1 ? snapshots.Peek() : snapshots.Dequeue();
            return ValueTask.FromResult<IEnumerable<Mail>>(snapshot);
        }

        public override ValueTask ConnectAsync(CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public override ValueTask DisconnectAsync(CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public override ValueTask SendAsync(IMail mail, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            IsDisposed = true;
        }
    }
}