using System.Threading.Channels;
using Atom.Net.Browsing.WebDriver.Protocol;
using Microsoft.Extensions.Logging;

namespace Atom.Net.Browsing.WebDriver;

internal sealed class BridgeServerState : IAsyncDisposable
{
    private readonly ILogger? logger;
    private readonly Channel<BridgeServerStateOperation> operations = Channel.CreateUnbounded<BridgeServerStateOperation>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });
    private readonly Dictionary<string, BridgeBrowserSession> sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BridgeTabChannel> tabs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BridgePendingRequest> pendingRequests = new(StringComparer.Ordinal);
    private readonly HashSet<string> settledRequestIds = new(StringComparer.Ordinal);
    private readonly Task processingTask;
    private long completedRequestCount;
    private long failedRequestCount;
    private bool isDisposed;

    public BridgeServerState(ILogger? logger = null)
    {
        this.logger = logger;
        processingTask = ProcessLoopAsync();
    }

    public ValueTask<SessionCreateResult> CreateSessionAsync(BridgeSessionDescriptor descriptor)
        => EnqueueAsync(descriptor, static (owner, state) => owner.CreateSessionCore(state));

    public ValueTask<SessionRemovalResult> RemoveSessionAsync(string sessionId)
        => EnqueueAsync(sessionId, static (owner, state) => owner.RemoveSessionCore(state));

    public ValueTask<BridgeBrowserSessionSnapshot?> CreateSessionSnapshotAsync(string sessionId)
        => EnqueueAsync(sessionId, static (owner, state) => owner.CreateSessionSnapshotCore(state));

    public ValueTask<TabRegistrationResult> RegisterTabAsync(BridgeTabChannelDescriptor descriptor)
        => EnqueueAsync(descriptor, static (owner, state) => owner.RegisterTabCore(state));

    public ValueTask<TabRemovalResult> UnregisterTabAsync(string sessionId, string tabId)
        => EnqueueAsync((sessionId, tabId), static (owner, state) => owner.UnregisterTabCore(state.sessionId, state.tabId));

    public ValueTask<BridgeTabChannelSnapshot?> CreateTabSnapshotAsync(string tabId)
        => EnqueueAsync(tabId, static (owner, state) => owner.CreateTabSnapshotCore(state));

    public ValueTask<BridgeTabChannelSnapshot[]> GetTabsForSessionAsync(string sessionId)
        => EnqueueAsync(sessionId, static (owner, state) => owner.GetTabsForSessionCore(state));

    public ValueTask<PendingRequestAddResult> AddPendingRequestAsync(BridgePendingRequestDescriptor descriptor)
        => EnqueueAsync(descriptor, static (owner, state) => owner.AddPendingRequestCore(state));

    public ValueTask<BridgePendingRequestSnapshot?> CreatePendingRequestSnapshotAsync(string messageId)
        => EnqueueAsync(messageId, static (owner, state) => owner.CreatePendingRequestSnapshotCore(state));

    public ValueTask<PendingRequestCompletionResult> TryCompletePendingRequestAsync(string messageId, BridgeMessage response)
        => EnqueueAsync((messageId, response), static (owner, state) => owner.TryCompletePendingRequestCore(state.messageId, state.response));

    public ValueTask<PendingRequestCompletionResult> TryFailPendingRequestAsync(string messageId, BridgeStatus status, string? error = null)
        => EnqueueAsync((messageId, status, error), static (owner, state) => owner.TryFailPendingRequestCore(state.messageId, state.status, state.error));

    public ValueTask<BulkFailureResult> FailRequestsForSessionAsync(string sessionId, BridgeStatus status, string? error = null)
        => EnqueueAsync((sessionId, status, error), static (owner, state) => owner.FailRequestsForSessionCore(state.sessionId, state.status, state.error));

    public ValueTask<BulkFailureResult> FailRequestsForTabAsync(string sessionId, string tabId, BridgeStatus status, string? error = null)
        => EnqueueAsync((sessionId, tabId, status, error), static (owner, state) => owner.FailRequestsForTabCore(state.sessionId, state.tabId, state.status, state.error));

    public ValueTask<BridgeServerHealthSnapshot> CreateHealthSnapshotAsync()
        => EnqueueAsync(operationState: false, action: static (owner, _) => owner.CreateHealthSnapshotCore());

    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        operations.Writer.TryComplete();
        await AwaitProcessingLoopAsync().ConfigureAwait(false);
    }

    private async Task ProcessLoopAsync()
    {
        await foreach (var operation in operations.Reader.ReadAllAsync().ConfigureAwait(false))
            operation.Execute(this);
    }

    private ValueTask<TResult> EnqueueAsync<TState, TResult>(TState operationState, Func<BridgeServerState, TState, TResult> action)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        var operation = new BridgeServerStateOperation<TState, TResult>(operationState, action);
        ObjectDisposedException.ThrowIf(condition: !operations.Writer.TryWrite(operation), instance: this);

        return new(task: operation.Task);
    }

    private ValueTask AwaitProcessingLoopAsync() => new(processingTask);

    private SessionCreateResult CreateSessionCore(BridgeSessionDescriptor descriptor)
    {
        if (!IsValid(descriptor))
            return new(Outcome: SessionCreateResultKind.InvalidDescriptor, Session: null);

        if (sessions.ContainsKey(descriptor.SessionId))
            return new(Outcome: SessionCreateResultKind.DuplicateSessionId, Session: null);

        var connectedAtUtc = descriptor.ConnectedAtUtc ?? DateTimeOffset.UtcNow;
        var session = new BridgeBrowserSession(
            descriptor.SessionId,
            descriptor.ProtocolVersion,
            connectedAtUtc,
            descriptor.BrowserFamily,
            descriptor.ExtensionVersion,
            descriptor.BrowserVersion);

        sessions.Add(session.SessionId, session);
        return new(Outcome: SessionCreateResultKind.Created, Session: CreateSessionSnapshotNoLock(session));
    }

    private SessionRemovalResult RemoveSessionCore(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return new(Outcome: SessionRemovalResultKind.SessionNotFound, Session: null, RemovedTabCount: 0, FailedPendingRequestCount: 0);

        if (!sessions.Remove(sessionId, out var session))
            return new(Outcome: SessionRemovalResultKind.SessionNotFound, Session: null, RemovedTabCount: 0, FailedPendingRequestCount: 0);

        session.IsConnected = false;
        session.LastSeenAtUtc = DateTimeOffset.UtcNow;

        var removedTabCount = session.ChannelsByTabId.Count;
        foreach (var tabId in session.ChannelsByTabId.Keys)
            tabs.Remove(tabId);

        session.ChannelsByTabId.Clear();
        var failedPendingRequestCount = FailRequestsNoLock(
            candidate => string.Equals(candidate.SessionId, sessionId, StringComparison.Ordinal),
            BridgeStatus.Disconnected,
            BridgeProtocolErrorCodes.SessionDisconnected);
        logger?.LogBridgeServerStateSessionRemoved(sessionId, removedTabCount, failedPendingRequestCount);

        return new(
            Outcome: SessionRemovalResultKind.Removed,
            Session: CreateSessionSnapshotNoLock(session),
            RemovedTabCount: removedTabCount,
            FailedPendingRequestCount: failedPendingRequestCount);
    }

    private BridgeBrowserSessionSnapshot? CreateSessionSnapshotCore(string sessionId)
        => sessions.TryGetValue(sessionId, out var session)
            ? CreateSessionSnapshotNoLock(session)
            : null;

    private TabRegistrationResult RegisterTabCore(BridgeTabChannelDescriptor descriptor)
    {
        if (!IsValid(descriptor))
            return new(Outcome: TabRegistrationResultKind.InvalidDescriptor, Tab: null);

        if (!sessions.TryGetValue(descriptor.SessionId, out var session))
            return new(Outcome: TabRegistrationResultKind.SessionNotFound, Tab: null);

        if (tabs.TryGetValue(descriptor.TabId, out var existing))
        {
            return string.Equals(existing.SessionId, descriptor.SessionId, StringComparison.Ordinal)
                ? new(Outcome: TabRegistrationResultKind.AlreadyOwnedBySession, Tab: CreateTabSnapshotNoLock(existing))
                : new(Outcome: TabRegistrationResultKind.DuplicateTabId, Tab: CreateTabSnapshotNoLock(existing));
        }

        var registeredAtUtc = descriptor.RegisteredAtUtc ?? DateTimeOffset.UtcNow;
        var channel = new BridgeTabChannel(descriptor.SessionId, descriptor.TabId, descriptor.WindowId, registeredAtUtc);

        session.LastSeenAtUtc = registeredAtUtc;
        session.ChannelsByTabId.Add(channel.TabId, channel);
        tabs.Add(channel.TabId, channel);
        return new(Outcome: TabRegistrationResultKind.Registered, Tab: CreateTabSnapshotNoLock(channel));
    }

    private TabRemovalResult UnregisterTabCore(string sessionId, string tabId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return new(Outcome: TabRemovalResultKind.SessionNotFound, Tab: null, FailedPendingRequestCount: 0);

        if (string.IsNullOrWhiteSpace(tabId))
            return new(Outcome: TabRemovalResultKind.TabNotFound, Tab: null, FailedPendingRequestCount: 0);

        if (!sessions.TryGetValue(sessionId, out var session))
            return new(Outcome: TabRemovalResultKind.SessionNotFound, Tab: null, FailedPendingRequestCount: 0);

        if (!tabs.TryGetValue(tabId, out var channel))
            return new(Outcome: TabRemovalResultKind.TabNotFound, Tab: null, FailedPendingRequestCount: 0);

        if (!string.Equals(channel.SessionId, sessionId, StringComparison.Ordinal))
            return new(Outcome: TabRemovalResultKind.TabOwnedByAnotherSession, Tab: CreateTabSnapshotNoLock(channel), FailedPendingRequestCount: 0);

        channel.IsRegistered = false;
        channel.LastSeenAtUtc = DateTimeOffset.UtcNow;
        session.LastSeenAtUtc = channel.LastSeenAtUtc;
        session.ChannelsByTabId.Remove(tabId);
        tabs.Remove(tabId);

        var failedPendingRequestCount = FailRequestsNoLock(
            candidate => string.Equals(candidate.SessionId, sessionId, StringComparison.Ordinal)
                && string.Equals(candidate.TabId, tabId, StringComparison.Ordinal),
            BridgeStatus.Disconnected,
            BridgeProtocolErrorCodes.TabDisconnected);
        logger?.LogBridgeServerStateTabRemoved(sessionId, tabId, failedPendingRequestCount);

        return new(Outcome: TabRemovalResultKind.Removed, Tab: CreateTabSnapshotNoLock(channel), FailedPendingRequestCount: failedPendingRequestCount);
    }

    private BridgeTabChannelSnapshot? CreateTabSnapshotCore(string tabId)
        => tabs.TryGetValue(tabId, out var channel)
            ? CreateTabSnapshotNoLock(channel)
            : null;

    private BridgeTabChannelSnapshot[] GetTabsForSessionCore(string sessionId)
    {
        if (!sessions.TryGetValue(sessionId, out var session))
            return [];

        return CreateTabSnapshotsNoLock(session.ChannelsByTabId.Values);
    }

    private PendingRequestAddResult AddPendingRequestCore(BridgePendingRequestDescriptor descriptor)
    {
        if (!IsValid(descriptor))
            return new(Outcome: PendingRequestAddResultKind.InvalidDescriptor, Request: null);

        if (pendingRequests.ContainsKey(descriptor.MessageId) || settledRequestIds.Contains(descriptor.MessageId))
            return new(Outcome: PendingRequestAddResultKind.DuplicateMessageId, Request: null);

        if (!sessions.TryGetValue(descriptor.SessionId, out var session))
            return new(Outcome: PendingRequestAddResultKind.SessionNotFound, Request: null);

        if (!session.ChannelsByTabId.TryGetValue(descriptor.TabId, out var channel))
            return new(Outcome: PendingRequestAddResultKind.TabNotFound, Request: null);

        var createdAtUtc = descriptor.CreatedAtUtc ?? DateTimeOffset.UtcNow;
        var pendingRequest = new BridgePendingRequest(
            descriptor.MessageId,
            descriptor.SessionId,
            descriptor.TabId,
            descriptor.Command,
            createdAtUtc,
            descriptor.DeadlineUtc,
            descriptor.CompletionSource);

        session.LastSeenAtUtc = createdAtUtc;
        channel.LastSeenAtUtc = createdAtUtc;
        pendingRequests.Add(pendingRequest.MessageId, pendingRequest);
        return new(Outcome: PendingRequestAddResultKind.Added, Request: CreatePendingRequestSnapshotNoLock(pendingRequest));
    }

    private BridgePendingRequestSnapshot? CreatePendingRequestSnapshotCore(string messageId)
        => pendingRequests.TryGetValue(messageId, out var pendingRequest)
            ? CreatePendingRequestSnapshotNoLock(pendingRequest)
            : null;

    private PendingRequestCompletionResult TryCompletePendingRequestCore(string messageId, BridgeMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!pendingRequests.Remove(messageId, out var pendingRequest))
        {
            return settledRequestIds.Contains(messageId)
                ? new(Outcome: PendingRequestCompletionResultKind.AlreadyCompleted, Request: null)
                : new(Outcome: PendingRequestCompletionResultKind.RequestNotFound, Request: null);
        }

        settledRequestIds.Add(messageId);
        pendingRequest.IsCompleted = true;
        completedRequestCount++;
        pendingRequest.CompletionSource.TrySetResult(response);
        return new(Outcome: PendingRequestCompletionResultKind.Completed, Request: CreatePendingRequestSnapshotNoLock(pendingRequest));
    }

    private PendingRequestCompletionResult TryFailPendingRequestCore(string messageId, BridgeStatus status, string? error)
    {
        if (!pendingRequests.Remove(messageId, out var pendingRequest))
        {
            return settledRequestIds.Contains(messageId)
                ? new(Outcome: PendingRequestCompletionResultKind.AlreadyCompleted, Request: null)
                : new(Outcome: PendingRequestCompletionResultKind.RequestNotFound, Request: null);
        }

        settledRequestIds.Add(messageId);
        pendingRequest.IsCompleted = true;
        failedRequestCount++;
        pendingRequest.CompletionSource.TrySetResult(CreateFailureResponse(messageId, status, error));
        return new(Outcome: PendingRequestCompletionResultKind.Completed, Request: CreatePendingRequestSnapshotNoLock(pendingRequest));
    }

    private BulkFailureResult FailRequestsForSessionCore(string sessionId, BridgeStatus status, string? error)
        => new(FailedRequestCount: FailRequestsNoLock(
            candidate => string.Equals(candidate.SessionId, sessionId, StringComparison.Ordinal),
            status,
            error));

    private BulkFailureResult FailRequestsForTabCore(string sessionId, string tabId, BridgeStatus status, string? error)
        => new(FailedRequestCount: FailRequestsNoLock(
            candidate => string.Equals(candidate.SessionId, sessionId, StringComparison.Ordinal)
                && string.Equals(candidate.TabId, tabId, StringComparison.Ordinal),
            status,
            error));

    private BridgeServerHealthSnapshot CreateHealthSnapshotCore()
        => new(
            SessionCount: sessions.Count,
            TabCount: tabs.Count,
            PendingRequestCount: pendingRequests.Count,
            CompletedRequestCount: completedRequestCount,
            FailedRequestCount: failedRequestCount);

    private int FailRequestsNoLock(Func<BridgePendingRequest, bool> predicate, BridgeStatus status, string? error)
    {
        var matchedIds = new List<string>(pendingRequests.Count);
        foreach (var pendingRequest in pendingRequests.Values)
        {
            if (predicate(pendingRequest))
                matchedIds.Add(pendingRequest.MessageId);
        }

        foreach (var messageId in matchedIds)
        {
            if (!pendingRequests.Remove(messageId, out var pendingRequest))
                continue;

            settledRequestIds.Add(messageId);
            pendingRequest.IsCompleted = true;
            failedRequestCount++;
            pendingRequest.CompletionSource.TrySetResult(CreateFailureResponse(messageId, status, error));
        }

        return matchedIds.Count;
    }

    private static BridgeMessage CreateFailureResponse(string messageId, BridgeStatus status, string? error)
        => new()
        {
            Id = messageId,
            Type = BridgeMessageType.Response,
            Status = status,
            Error = error,
        };

    private static BridgeBrowserSessionSnapshot CreateSessionSnapshotNoLock(BridgeBrowserSession session)
        => new(
            SessionId: session.SessionId,
            ProtocolVersion: session.ProtocolVersion,
            ConnectedAtUtc: session.ConnectedAtUtc,
            LastSeenAtUtc: session.LastSeenAtUtc,
            BrowserFamily: session.BrowserFamily,
            ExtensionVersion: session.ExtensionVersion,
            BrowserVersion: session.BrowserVersion,
            IsConnected: session.IsConnected,
            Tabs: CreateTabSnapshotsNoLock(session.ChannelsByTabId.Values));

    private static BridgeTabChannelSnapshot[] CreateTabSnapshotsNoLock(Dictionary<string, BridgeTabChannel>.ValueCollection channels)
    {
        var snapshots = new BridgeTabChannelSnapshot[channels.Count];
        var index = 0;

        foreach (var channel in channels)
        {
            snapshots[index] = CreateTabSnapshotNoLock(channel);
            index++;
        }

        return snapshots;
    }

    private static BridgeTabChannelSnapshot CreateTabSnapshotNoLock(BridgeTabChannel channel)
        => new(
            SessionId: channel.SessionId,
            TabId: channel.TabId,
            WindowId: channel.WindowId,
            RegisteredAtUtc: channel.RegisteredAtUtc,
            LastSeenAtUtc: channel.LastSeenAtUtc,
            IsRegistered: channel.IsRegistered);

    private static BridgePendingRequestSnapshot CreatePendingRequestSnapshotNoLock(BridgePendingRequest pendingRequest)
        => new(
            MessageId: pendingRequest.MessageId,
            SessionId: pendingRequest.SessionId,
            TabId: pendingRequest.TabId,
            Command: pendingRequest.Command,
            CreatedAtUtc: pendingRequest.CreatedAtUtc,
            DeadlineUtc: pendingRequest.DeadlineUtc,
            IsCompleted: pendingRequest.IsCompleted);

    private static bool IsValid(BridgeSessionDescriptor descriptor)
        => !string.IsNullOrWhiteSpace(descriptor.SessionId)
            && descriptor.ProtocolVersion > 0
            && !string.IsNullOrWhiteSpace(descriptor.BrowserFamily)
            && !string.IsNullOrWhiteSpace(descriptor.ExtensionVersion);

    private static bool IsValid(BridgeTabChannelDescriptor descriptor)
        => !string.IsNullOrWhiteSpace(descriptor.SessionId)
            && !string.IsNullOrWhiteSpace(descriptor.TabId);

    private static bool IsValid(BridgePendingRequestDescriptor descriptor)
        => !string.IsNullOrWhiteSpace(descriptor.MessageId)
            && !string.IsNullOrWhiteSpace(descriptor.SessionId)
            && !string.IsNullOrWhiteSpace(descriptor.TabId)
            && Enum.IsDefined(descriptor.Command);

    private abstract class BridgeServerStateOperation
    {
        public abstract void Execute(BridgeServerState owner);
    }

    private sealed class BridgeServerStateOperation<TState, TResult>(TState state, Func<BridgeServerState, TState, TResult> action) : BridgeServerStateOperation
    {
        private readonly TaskCompletionSource<TResult> completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TResult> Task => completionSource.Task;

        public override void Execute(BridgeServerState owner)
        {
            try
            {
                completionSource.TrySetResult(action(owner, state));
            }
            catch (Exception exception)
            {
                completionSource.TrySetException(exception);
            }
        }
    }
}