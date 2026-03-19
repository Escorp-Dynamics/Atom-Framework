using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Atom.Net.Https.Connections;

namespace Atom.Net.Https;

public sealed partial class HttpsClientHandler
{
    private bool CanReuseConnection(Https11Connection connection, HttpsResponseMessage response)
        => response.Exception is null
        && connection.IsConnected
        && !connection.IsDraining
        && connection.HasCapacity
        && !IsConnectionExpired(connection)
        && !IsConnectionLifetimeExpired(connection)
        && MaxConnectionsPerServer > 0;

    private Https11Connection? TryRentConnection(ConnectionPoolKey key, ConnectionPoolState poolState)
    {
        while (poolState.Connections.TryDequeue(out var connection))
        {
            if (IsConnectionExpired(connection) || IsConnectionLifetimeExpired(connection))
            {
                DisposeConnection(connection);
                continue;
            }

            if (connection.IsConnected && connection.MatchesTarget(key.Host, key.Port, key.IsHttps) && connection.HasCapacity)
                return connection;

            DisposeConnection(connection);
        }

        return null;
    }

    private void ReturnConnection(ConnectionPoolState poolState, Https11Connection connection)
    {
        if (MaxConnectionsPerServer <= 0 || IsConnectionExpired(connection) || IsConnectionLifetimeExpired(connection))
        {
            DisposeConnection(connection);
            return;
        }

        while (poolState.Connections.Count >= MaxConnectionsPerServer && poolState.Connections.TryDequeue(out var extraConnection))
            DisposeConnection(extraConnection);

        poolState.Connections.Enqueue(connection);
    }

    private static void DisposeConnection(Https11Connection connection) => connection.Dispose();

    private static ValueTask DisposeConnectionAsync(Https11Connection connection) => connection.DisposeAsync();

    private bool IsConnectionExpired(Https11Connection connection)
    {
        if (PooledConnectionIdleTimeout == Timeout.InfiniteTimeSpan) return false;
        if (PooledConnectionIdleTimeout <= TimeSpan.Zero) return true;

        var lastActivity = connection.LastActivityTimestamp;
        if (lastActivity <= 0) return true;

        return Stopwatch.GetElapsedTime(lastActivity) >= PooledConnectionIdleTimeout;
    }

    private bool IsConnectionLifetimeExpired(Https11Connection connection)
    {
        if (PooledConnectionLifetime == Timeout.InfiniteTimeSpan) return false;
        if (PooledConnectionLifetime <= TimeSpan.Zero) return true;

        var created = connection.CreatedTimestamp;
        if (created <= 0) return true;

        return Stopwatch.GetElapsedTime(created) >= PooledConnectionLifetime;
    }

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Pool semaphores stay alive for the handler lifetime because active requests may still release slots after pool teardown begins.")]
    private sealed class ConnectionPoolState : IDisposable
    {
        private readonly SemaphoreSlim? slots;

        public ConnectionPoolState(int maxConnections)
        {
            if (maxConnections is > 0 and not int.MaxValue)
                slots = new SemaphoreSlim(maxConnections, maxConnections);
        }

        public ConcurrentQueue<Https11Connection> Connections { get; } = new();

        public ValueTask WaitForLeaseAsync(CancellationToken cancellationToken)
            => slots is null ? ValueTask.CompletedTask : new ValueTask(slots.WaitAsync(cancellationToken));

        public void ReleaseLease()
        {
            if (slots is null) return;
            slots.Release();
        }

        public void Dispose()
        {
        }
    }

    private readonly record struct ConnectionPoolStateFactoryArg(int MaxConnections);
    private readonly record struct ConnectionPoolKey(string Host, int Port, bool IsHttps);
}