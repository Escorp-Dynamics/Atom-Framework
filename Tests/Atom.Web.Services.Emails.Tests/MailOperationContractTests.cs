using Atom.Web.Emails;
using Atom.Web.Emails.Services;

namespace Atom.Web.Emails.Tests;

public class MailOperationContractTests(ILogger logger) : BenchmarkTests<MailOperationContractTests>(logger)
{
    public MailOperationContractTests() : this(ConsoleLogger.Unicode) { }

    [TestCase(TestName = "TemporaryEmailAccount дедуплицирует inbox и поднимает MailReceived только для новых писем"), Benchmark]
    public async Task TemporaryEmailAccountDeduplicatesInboxAndRaisesMailReceivedForNewMessagesOnlyTest()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();

        using var account = new StubTemporaryEmailAccount([
            [
                new Mail(firstId, "a@example.com", "atom@example.test", "One", "Body 1"),
                new Mail(firstId, "a@example.com", "atom@example.test", "One", "Body 1 duplicate"),
                new Mail(secondId, "b@example.com", "atom@example.test", "Two", "Body 2"),
            ],
            [
                new Mail(secondId, "b@example.com", "atom@example.test", "Two", "Body 2"),
                new Mail(thirdId, "c@example.com", "atom@example.test", "Three", "Body 3"),
            ],
        ]);

        var receivedSubjects = new List<string>();
        account.MailReceived += (_, args) => receivedSubjects.Add(args.Mail.Subject);

        var firstSnapshot = (await account.RefreshInboxAsync(CancellationToken.None)).ToArray();
        var secondSnapshot = (await account.RefreshInboxAsync(CancellationToken.None)).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstSnapshot.Select(static mail => mail.Subject), Is.EqualTo(["One", "Two"]));
            Assert.That(secondSnapshot.Select(static mail => mail.Subject), Is.EqualTo(["Two", "Three"]));
            Assert.That(receivedSubjects, Is.EqualTo(["One", "Two", "Three"]));
            Assert.That(account.Count, Is.EqualTo(2));
            Assert.That(account.CanSend, Is.False);
        }
    }

    [TestCase(TestName = "HttpTemporaryEmailMail делегирует mark-as-read и delete в upstream operations"), Benchmark]
    public async Task HttpTemporaryEmailMailDelegatesReadAndDeleteToUpstreamOperationsTest()
    {
        var operations = new StubHttpMailOperations();
        var mail = new StubHttpTemporaryEmailMail(operations, "up-1", Guid.NewGuid(), "from@example.com", "to@example.com", "Hello", "Body");

        await mail.MarkAsReadAsync(CancellationToken.None);
        await mail.DeleteAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mail.IsRead, Is.True);
            Assert.That(operations.MarkedAsReadIds, Is.EqualTo(["up-1"]));
            Assert.That(operations.DeletedIds, Is.EqualTo(["up-1"]));
        }
    }

    private sealed class StubTemporaryEmailAccount(IEnumerable<IEnumerable<Mail>> snapshots)
        : TemporaryEmailAccount(Guid.NewGuid(), "atom@example.test")
    {
        private readonly Queue<Mail[]> snapshots = new(snapshots.Select(static snapshot => snapshot.ToArray()));

        public override ValueTask ConnectAsync(CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public override ValueTask DisconnectAsync(CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public override ValueTask SendAsync(IMail mail, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        protected override ValueTask<IEnumerable<Mail>> LoadMessagesAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult<IEnumerable<Mail>>(snapshots.Count > 0 ? snapshots.Dequeue() : []);
    }

    private sealed class StubHttpMailOperations : IHttpTemporaryEmailMailOperations
    {
        public List<string> MarkedAsReadIds { get; } = [];

        public List<string> DeletedIds { get; } = [];

        public ValueTask MarkUpstreamMailAsReadAsync(string upstreamMessageId, CancellationToken cancellationToken)
        {
            MarkedAsReadIds.Add(upstreamMessageId);
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteUpstreamMailAsync(string upstreamMessageId, CancellationToken cancellationToken)
        {
            DeletedIds.Add(upstreamMessageId);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubHttpTemporaryEmailMail(
        StubHttpMailOperations operations,
        string upstreamId,
        Guid id,
        string from,
        string to,
        string subject,
        string body)
        : HttpTemporaryEmailMail<StubHttpMailOperations>(operations, upstreamId, id, from, to, subject, body, isRead: false);
}