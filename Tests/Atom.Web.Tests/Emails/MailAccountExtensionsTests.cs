using Atom.Web.Emails;

namespace Atom.Web.Tests;

public class MailAccountExtensionsTests(ILogger logger) : BenchmarkTests<MailAccountExtensionsTests>(logger)
{
    public MailAccountExtensionsTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "EnsureCanSend бросает исключение для receive-only аккаунта"), Benchmark]
    public void EnsureCanSendThrowsForReceiveOnlyAccountTest()
    {
        var account = new StubMailAccount(canSend: false);

        var exception = Assert.Throws<NotSupportedException>(() => account.EnsureCanSend());

        Assert.That(exception?.Message, Is.EqualTo($"Почтовый аккаунт '{account.Address}' не поддерживает исходящую отправку."));
    }

    [TestCase(TestName = "SendCheckedAsync валидирует capability и делегирует отправку"), Benchmark]
    public async Task SendCheckedAsyncValidatesCapabilityAndDelegatesTest()
    {
        var account = new StubMailAccount(canSend: true);
        var mail = new Mail("from@example.com", "to@example.com", "Subject", "Body");

        await account.SendCheckedAsync(mail, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(account.LastSentMail, Is.SameAs(mail));
            Assert.That(account.SendCalls, Is.EqualTo(1));
        }
    }

    private sealed class StubMailAccount(bool canSend) : MailAccount(Guid.NewGuid(), "atom", "password", "atom@example.test")
    {
        public override IEnumerable<Mail> Inbox => [];

        public override int Count => 0;

        public override bool CanSend => canSend;

        public override event MutableEventHandler<IMailAccount, MailReceivedEventArgs>? MailReceived
        {
            add { }
            remove { }
        }

        public IMail? LastSentMail { get; private set; }

        public int SendCalls { get; private set; }

        public override ValueTask ConnectAsync(CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public override ValueTask DisconnectAsync(CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public override ValueTask<IEnumerable<Mail>> RefreshInboxAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult<IEnumerable<Mail>>([]);

        public override ValueTask SendAsync(IMail mail, CancellationToken cancellationToken)
        {
            LastSentMail = mail;
            SendCalls++;
            return ValueTask.CompletedTask;
        }
    }
}