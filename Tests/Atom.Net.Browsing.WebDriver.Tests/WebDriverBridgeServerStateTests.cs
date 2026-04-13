using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver.Tests;

public sealed class WebDriverBridgeServerStateTests
{
    [Test]
    public async Task CreateSessionRejectsDuplicateSessionId()
    {
        await using var state = new BridgeServerState();
        var descriptor = CreateSessionDescriptor("session-a");

        var first = await state.CreateSessionAsync(descriptor).ConfigureAwait(false);
        var second = await state.CreateSessionAsync(descriptor).ConfigureAwait(false);
        var snapshot = await state.CreateHealthSnapshotAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(first.Outcome, Is.EqualTo(SessionCreateResultKind.Created));
            Assert.That(second.Outcome, Is.EqualTo(SessionCreateResultKind.DuplicateSessionId));
            Assert.That(snapshot.SessionCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RegisterTabRejectsDuplicateTabOwnedByAnotherSession()
    {
        await using var state = new BridgeServerState();
        await state.CreateSessionAsync(CreateSessionDescriptor("session-a")).ConfigureAwait(false);
        await state.CreateSessionAsync(CreateSessionDescriptor("session-b")).ConfigureAwait(false);

        var first = await state.RegisterTabAsync(new BridgeTabChannelDescriptor("session-a", "tab-1", "window-1")).ConfigureAwait(false);
        var second = await state.RegisterTabAsync(new BridgeTabChannelDescriptor("session-b", "tab-1", "window-2")).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(first.Outcome, Is.EqualTo(TabRegistrationResultKind.Registered));
            Assert.That(second.Outcome, Is.EqualTo(TabRegistrationResultKind.DuplicateTabId));
            Assert.That(second.Tab?.SessionId, Is.EqualTo("session-a"));
        });
    }

    [Test]
    public async Task AddPendingRequestRejectsDuplicateMessageId()
    {
        await using var state = new BridgeServerState();
        await state.CreateSessionAsync(CreateSessionDescriptor("session-a")).ConfigureAwait(false);
        await state.RegisterTabAsync(new BridgeTabChannelDescriptor("session-a", "tab-1")).ConfigureAwait(false);

        var first = await state.AddPendingRequestAsync(new BridgePendingRequestDescriptor("request-1", "session-a", "tab-1")).ConfigureAwait(false);
        var second = await state.AddPendingRequestAsync(new BridgePendingRequestDescriptor("request-1", "session-a", "tab-1")).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(first.Outcome, Is.EqualTo(PendingRequestAddResultKind.Added));
            Assert.That(second.Outcome, Is.EqualTo(PendingRequestAddResultKind.DuplicateMessageId));
        });
    }

    [Test]
    public async Task CompletePendingRequestSucceedsOnce()
    {
        await using var state = new BridgeServerState();
        await state.CreateSessionAsync(CreateSessionDescriptor("session-a")).ConfigureAwait(false);
        await state.RegisterTabAsync(new BridgeTabChannelDescriptor("session-a", "tab-1")).ConfigureAwait(false);

        var completionSource = new TaskCompletionSource<BridgeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await state.AddPendingRequestAsync(new BridgePendingRequestDescriptor("request-1", "session-a", "tab-1", CompletionSource: completionSource)).ConfigureAwait(false);

        var response = new BridgeMessage
        {
            Id = "request-1",
            Type = BridgeMessageType.Response,
            Status = BridgeStatus.Ok,
        };

        var first = await state.TryCompletePendingRequestAsync("request-1", response).ConfigureAwait(false);
        var second = await state.TryCompletePendingRequestAsync("request-1", response).ConfigureAwait(false);
        var completed = await completionSource.Task.ConfigureAwait(false);
        var snapshot = await state.CreateHealthSnapshotAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(first.Outcome, Is.EqualTo(PendingRequestCompletionResultKind.Completed));
            Assert.That(second.Outcome, Is.EqualTo(PendingRequestCompletionResultKind.AlreadyCompleted));
            Assert.That(completed.Status, Is.EqualTo(BridgeStatus.Ok));
            Assert.That(snapshot.PendingRequestCount, Is.Zero);
            Assert.That(snapshot.CompletedRequestCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RemoveSessionRemovesOwnedTabsAndRequests()
    {
        await using var state = new BridgeServerState();
        await state.CreateSessionAsync(CreateSessionDescriptor("session-a")).ConfigureAwait(false);
        await state.RegisterTabAsync(new BridgeTabChannelDescriptor("session-a", "tab-1")).ConfigureAwait(false);

        var completionSource = new TaskCompletionSource<BridgeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await state.AddPendingRequestAsync(new BridgePendingRequestDescriptor("request-1", "session-a", "tab-1", CompletionSource: completionSource)).ConfigureAwait(false);

        var result = await state.RemoveSessionAsync("session-a").ConfigureAwait(false);
        var completed = await completionSource.Task.ConfigureAwait(false);
        var snapshot = await state.CreateHealthSnapshotAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(SessionRemovalResultKind.Removed));
            Assert.That(result.RemovedTabCount, Is.EqualTo(1));
            Assert.That(result.FailedPendingRequestCount, Is.EqualTo(1));
            Assert.That(completed.Status, Is.EqualTo(BridgeStatus.Disconnected));
            Assert.That(snapshot.SessionCount, Is.Zero);
            Assert.That(snapshot.TabCount, Is.Zero);
            Assert.That(snapshot.PendingRequestCount, Is.Zero);
            Assert.That(snapshot.FailedRequestCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task HealthSnapshotReflectsCounts()
    {
        await using var state = new BridgeServerState();
        await state.CreateSessionAsync(CreateSessionDescriptor("session-a")).ConfigureAwait(false);
        await state.CreateSessionAsync(CreateSessionDescriptor("session-b")).ConfigureAwait(false);
        await state.RegisterTabAsync(new BridgeTabChannelDescriptor("session-a", "tab-1")).ConfigureAwait(false);
        await state.RegisterTabAsync(new BridgeTabChannelDescriptor("session-b", "tab-2")).ConfigureAwait(false);
        await state.AddPendingRequestAsync(new BridgePendingRequestDescriptor("request-1", "session-a", "tab-1")).ConfigureAwait(false);

        var snapshot = await state.CreateHealthSnapshotAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.SessionCount, Is.EqualTo(2));
            Assert.That(snapshot.TabCount, Is.EqualTo(2));
            Assert.That(snapshot.PendingRequestCount, Is.EqualTo(1));
            Assert.That(snapshot.CompletedRequestCount, Is.Zero);
            Assert.That(snapshot.FailedRequestCount, Is.Zero);
        });
    }

    private static BridgeSessionDescriptor CreateSessionDescriptor(string sessionId)
        => new(sessionId, 1, "chromium", "1.0.0");
}