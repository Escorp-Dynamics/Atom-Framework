using Atom.Net.Browsing.WebDriver.Protocol;

namespace Atom.Net.Browsing.WebDriver.Tests;

public sealed class WebDriverBridgeServerStateSkeletonTests
{
    [Test]
    public async Task CreateSessionRejectsDuplicateSessionId()
    {
        await using var state = new BridgeServerState();

        var first = await state.CreateSessionAsync(CreateSessionDescriptor("session-a")).ConfigureAwait(false);
        var second = await state.CreateSessionAsync(CreateSessionDescriptor("session-a")).ConfigureAwait(false);
        var health = await state.CreateHealthSnapshotAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(first.Outcome, Is.EqualTo(SessionCreateResultKind.Created));
            Assert.That(second.Outcome, Is.EqualTo(SessionCreateResultKind.DuplicateSessionId));
            Assert.That(health.SessionCount, Is.EqualTo(1));
            Assert.That(health.TabCount, Is.Zero);
        });
    }

    [Test]
    public async Task RegisterTabRejectsDuplicateTabOwnedByAnotherSession()
    {
        await using var state = new BridgeServerState();
        await state.CreateSessionAsync(CreateSessionDescriptor("session-a")).ConfigureAwait(false);
        await state.CreateSessionAsync(CreateSessionDescriptor("session-b")).ConfigureAwait(false);

        var first = await state.RegisterTabAsync(new BridgeTabChannelDescriptor("session-a", "tab-1")).ConfigureAwait(false);
        var second = await state.RegisterTabAsync(new BridgeTabChannelDescriptor("session-b", "tab-1")).ConfigureAwait(false);
        var health = await state.CreateHealthSnapshotAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(first.Outcome, Is.EqualTo(TabRegistrationResultKind.Registered));
            Assert.That(second.Outcome, Is.EqualTo(TabRegistrationResultKind.DuplicateTabId));
            Assert.That(second.Tab?.SessionId, Is.EqualTo("session-a"));
            Assert.That(health.TabCount, Is.EqualTo(1));
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
        var health = await state.CreateHealthSnapshotAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(first.Outcome, Is.EqualTo(PendingRequestAddResultKind.Added));
            Assert.That(second.Outcome, Is.EqualTo(PendingRequestAddResultKind.DuplicateMessageId));
            Assert.That(health.PendingRequestCount, Is.EqualTo(1));
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
        var health = await state.CreateHealthSnapshotAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(first.Outcome, Is.EqualTo(PendingRequestCompletionResultKind.Completed));
            Assert.That(second.Outcome, Is.EqualTo(PendingRequestCompletionResultKind.AlreadyCompleted));
            Assert.That(completed.Status, Is.EqualTo(BridgeStatus.Ok));
            Assert.That(health.PendingRequestCount, Is.Zero);
            Assert.That(health.CompletedRequestCount, Is.EqualTo(1));
            Assert.That(health.FailedRequestCount, Is.Zero);
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
        var failed = await completionSource.Task.ConfigureAwait(false);
        var health = await state.CreateHealthSnapshotAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(SessionRemovalResultKind.Removed));
            Assert.That(result.RemovedTabCount, Is.EqualTo(1));
            Assert.That(result.FailedPendingRequestCount, Is.EqualTo(1));
            Assert.That(failed.Status, Is.EqualTo(BridgeStatus.Disconnected));
            Assert.That(health.SessionCount, Is.Zero);
            Assert.That(health.TabCount, Is.Zero);
            Assert.That(health.PendingRequestCount, Is.Zero);
            Assert.That(health.FailedRequestCount, Is.EqualTo(1));
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

        var health = await state.CreateHealthSnapshotAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(health.SessionCount, Is.EqualTo(2));
            Assert.That(health.TabCount, Is.EqualTo(2));
            Assert.That(health.PendingRequestCount, Is.EqualTo(1));
            Assert.That(health.CompletedRequestCount, Is.Zero);
            Assert.That(health.FailedRequestCount, Is.Zero);
        });
    }

    [Test]
    public async Task RegisterTabFailsWhenSessionMissing()
    {
        await using var state = new BridgeServerState();

        var result = await state.RegisterTabAsync(new BridgeTabChannelDescriptor("missing-session", "tab-1")).ConfigureAwait(false);
        var health = await state.CreateHealthSnapshotAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(TabRegistrationResultKind.SessionNotFound));
            Assert.That(health.SessionCount, Is.Zero);
            Assert.That(health.TabCount, Is.Zero);
        });
    }

    [Test]
    public async Task UnregisterTabRejectsForeignOwner()
    {
        await using var state = new BridgeServerState();
        await state.CreateSessionAsync(CreateSessionDescriptor("session-a")).ConfigureAwait(false);
        await state.CreateSessionAsync(CreateSessionDescriptor("session-b")).ConfigureAwait(false);
        await state.RegisterTabAsync(new BridgeTabChannelDescriptor("session-a", "tab-1")).ConfigureAwait(false);

        var result = await state.UnregisterTabAsync("session-b", "tab-1").ConfigureAwait(false);
        var health = await state.CreateHealthSnapshotAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(TabRemovalResultKind.TabOwnedByAnotherSession));
            Assert.That(result.Tab?.SessionId, Is.EqualTo("session-a"));
            Assert.That(health.SessionCount, Is.EqualTo(2));
            Assert.That(health.TabCount, Is.EqualTo(1));
            Assert.That(health.PendingRequestCount, Is.Zero);
        });
    }

    [Test]
    public async Task RemoveSessionReturnsNotFoundForUnknownId()
    {
        await using var state = new BridgeServerState();

        var result = await state.RemoveSessionAsync("missing-session").ConfigureAwait(false);
        var health = await state.CreateHealthSnapshotAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(SessionRemovalResultKind.SessionNotFound));
            Assert.That(health.SessionCount, Is.Zero);
            Assert.That(health.TabCount, Is.Zero);
        });
    }

    [Test]
    public async Task RemoveSessionFailsOwnedPendingRequestsAsDisconnected()
    {
        await using var state = new BridgeServerState();
        await state.CreateSessionAsync(CreateSessionDescriptor("session-a")).ConfigureAwait(false);
        await state.RegisterTabAsync(new BridgeTabChannelDescriptor("session-a", "tab-1")).ConfigureAwait(false);

        var completionSource = new TaskCompletionSource<BridgeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await state.AddPendingRequestAsync(new BridgePendingRequestDescriptor("request-1", "session-a", "tab-1", CompletionSource: completionSource)).ConfigureAwait(false);

        _ = await state.RemoveSessionAsync("session-a").ConfigureAwait(false);
        var failed = await completionSource.Task.ConfigureAwait(false);
        var health = await state.CreateHealthSnapshotAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Status, Is.EqualTo(BridgeStatus.Disconnected));
            Assert.That(failed.Error, Is.EqualTo(BridgeProtocolErrorCodes.SessionDisconnected));
            Assert.That(health.PendingRequestCount, Is.Zero);
            Assert.That(health.FailedRequestCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task FailedPendingRequestsDoNotStayInHealthSnapshot()
    {
        await using var state = new BridgeServerState();
        await state.CreateSessionAsync(CreateSessionDescriptor("session-a")).ConfigureAwait(false);
        await state.RegisterTabAsync(new BridgeTabChannelDescriptor("session-a", "tab-1")).ConfigureAwait(false);

        var completionSource = new TaskCompletionSource<BridgeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await state.AddPendingRequestAsync(new BridgePendingRequestDescriptor("request-1", "session-a", "tab-1", CompletionSource: completionSource)).ConfigureAwait(false);

        var failure = await state.TryFailPendingRequestAsync("request-1", BridgeStatus.Error, "failed").ConfigureAwait(false);
        var failed = await completionSource.Task.ConfigureAwait(false);
        var health = await state.CreateHealthSnapshotAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(failure.Outcome, Is.EqualTo(PendingRequestCompletionResultKind.Completed));
            Assert.That(failed.Status, Is.EqualTo(BridgeStatus.Error));
            Assert.That(failed.Error, Is.EqualTo("failed"));
            Assert.That(health.PendingRequestCount, Is.Zero);
            Assert.That(health.CompletedRequestCount, Is.Zero);
            Assert.That(health.FailedRequestCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SessionCleanupIsIdempotentAtStateLayer()
    {
        await using var state = new BridgeServerState();
        await state.CreateSessionAsync(CreateSessionDescriptor("session-a")).ConfigureAwait(false);

        var first = await state.RemoveSessionAsync("session-a").ConfigureAwait(false);
        var second = await state.RemoveSessionAsync("session-a").ConfigureAwait(false);
        var health = await state.CreateHealthSnapshotAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(first.Outcome, Is.EqualTo(SessionRemovalResultKind.Removed));
            Assert.That(second.Outcome, Is.EqualTo(SessionRemovalResultKind.SessionNotFound));
            Assert.That(health.SessionCount, Is.Zero);
            Assert.That(health.TabCount, Is.Zero);
            Assert.That(health.PendingRequestCount, Is.Zero);
        });
    }

    [Test]
    public async Task ReplayedUnregisterAfterSessionCleanupDoesNotCorruptState()
    {
        await using var state = new BridgeServerState();
        await state.CreateSessionAsync(CreateSessionDescriptor("session-a")).ConfigureAwait(false);
        await state.RegisterTabAsync(new BridgeTabChannelDescriptor("session-a", "tab-1")).ConfigureAwait(false);

        _ = await state.RemoveSessionAsync("session-a").ConfigureAwait(false);
        var result = await state.UnregisterTabAsync("session-a", "tab-1").ConfigureAwait(false);
        var health = await state.CreateHealthSnapshotAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(TabRemovalResultKind.SessionNotFound));
            Assert.That(health.SessionCount, Is.Zero);
            Assert.That(health.TabCount, Is.Zero);
            Assert.That(health.PendingRequestCount, Is.Zero);
        });
    }

    private static BridgeSessionDescriptor CreateSessionDescriptor(string sessionId)
        => new(sessionId, 1, "chromium", "1.0.0");
}