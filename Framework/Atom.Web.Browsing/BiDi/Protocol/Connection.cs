using System.Net.WebSockets;
using System.Text;
using Atom.Threading;

namespace Atom.Web.Browsing.BiDi.Protocol;

/// <summary>
/// Represents a connection to a WebDriver Bidi remote end.
/// </summary>
public class Connection : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private readonly Locker locker = new();
    private Task? dataReceiveTask;
    private ClientWebSocket client = new();
    private CancellationTokenSource cts = new();
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="Connection"/> class.
    /// </summary>
    public Connection() { }

    /// <summary>
    /// Gets a value indicating whether this connection is active.
    /// </summary>
    public bool IsActive => client.State is not WebSocketState.None and not WebSocketState.Closed and not WebSocketState.Aborted;

    /// <summary>
    /// Gets the buffer size for communication used by this connection.
    /// </summary>
    public int BufferSize { get; } = 4096;

    /// <summary>
    /// Gets or sets the WebSocket URL to which the connection is connected.
    /// </summary>
    public Uri? ConnectedUrl { get; protected set; }

    /// <summary>
    /// Gets or sets the value of the timeout to wait before throwing an error when starting up the connection.
    /// </summary>
    public TimeSpan StartupTimeout { get; set; } = DefaultTimeout;

    /// <summary>
    /// Gets or sets the value of the timeout to wait before throwing an error when shutting down the connection.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = DefaultTimeout;

    /// <summary>
    /// Gets or sets the value of the timeout to wait for exclusive access when sending to or receiving data from the ClientWebSocket.
    /// </summary>
    public TimeSpan DataTimeout { get; set; } = DefaultTimeout;

    /// <summary>
    /// Gets an observable event that notifies when data is received from this connection.
    /// </summary>
    public ObservableEvent<ConnectionDataReceivedEventArgs> OnDataReceived { get; } = new();

    /// <summary>
    /// Gets an observable event that notifies when a log message is written.
    /// </summary>
    public ObservableEvent<LogMessageEventArgs> OnLogMessage { get; } = new();

    private async ValueTask ReceiveDataAsync()
    {
        var cancellationToken = cts.Token;

        try
        {
            MemoryStream? ms = null;
            var buffer = WebSocket.CreateClientBuffer(BufferSize, BufferSize);

            while (client.State is not WebSocketState.Closed && !cancellationToken.IsCancellationRequested)
            {
                var receiveResult = await client.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);

                if (!cancellationToken.IsCancellationRequested)
                {
                    if (receiveResult.MessageType is WebSocketMessageType.Close && client.State is not WebSocketState.Closed and not WebSocketState.CloseSent)
                    {
                        await LogAsync($"Acknowledging Close frame received from server (client state: {client.State})").ConfigureAwait(false);
                        await client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Acknowledge Close frame", CancellationToken.None).ConfigureAwait(false);
                    }

                    if (client.State is WebSocketState.Open && receiveResult.MessageType is not WebSocketMessageType.Close)
                    {
                        if (ms is null && !receiveResult.EndOfMessage) ms = new MemoryStream();

                        var seg = buffer.AsMemory(0, receiveResult.Count);
                        if (ms is not null) await ms.WriteAsync(seg).ConfigureAwait(false);

                        if (receiveResult.EndOfMessage)
                        {
                            var bytes = ms is null ? seg : ms.ToArray();

                            if (ms is not null)
                            {
                                await ms.DisposeAsync().ConfigureAwait(false);
                                ms = default;
                            }

                            if (bytes.Length > 0)
                            {
                                if (OnLogMessage.CurrentObserverCount > 0)
                                    await LogAsync($"RECV <<< {Encoding.UTF8.GetString(bytes.Span)}", BiDiLogLevel.Debug).ConfigureAwait(false);

                                await OnDataReceived.NotifyObserversAsync(new ConnectionDataReceivedEventArgs(bytes)).ConfigureAwait(false);
                            }
                        }
                    }
                }
            }

            await LogAsync($"Ending processing loop in state {client.State}").ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // An OperationCanceledException is normal upon task/token cancellation, so disregard it
        }
        catch (WebSocketException e)
        {
            await LogAsync($"Unexpected error during receive of data: {e.Message}").ConfigureAwait(false);
        }
        finally
        {
            client.Dispose();
        }
    }

    private ValueTask LogAsync(string message, BiDiLogLevel level) => OnLogMessage.NotifyObserversAsync(new LogMessageEventArgs(message, level, "Connection"));

    private ValueTask LogAsync(string message) => LogAsync(message, BiDiLogLevel.Info);

    /// <summary>
    /// Asynchronously sends data to the underlying WebSocket of this connection.
    /// </summary>
    /// <param name="data">The buffer containing the data to be sent to the remote end of this connection via the WebSocket.</param>
    /// <returns>The task object representing the asynchronous operation.</returns>
    protected virtual ValueTask SendWebSocketDataAsync(ReadOnlyMemory<byte> data) => client.SendAsync(data, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

    /// <summary>
    /// Asynchronously closes the client WebSocket.
    /// </summary>
    /// <returns>The task object representing the asynchronous operation.</returns>
    protected virtual async ValueTask CloseClientWebSocketAsync()
    {
        using var timeout = new CancellationTokenSource(ShutdownTimeout);

        try
        {
            await client.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", timeout.Token).ConfigureAwait(false);

            while (client.State is not WebSocketState.Closed and not WebSocketState.Aborted && !timeout.Token.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);

            await LogAsync($"Client state is {client.State}").ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // An OperationCanceledException is normal upon task/token cancellation, so disregard it
        }
    }

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    /// <param name="disposing">Указывает, требуется ли освободить управляемые ресурсы.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;

        if (disposing)
        {
            client.Dispose();
            cts.Dispose();
            locker.Dispose();
        }
    }

    /// <summary>
    /// Asynchronously starts communication with the remote end of this connection.
    /// </summary>
    /// <param name="url">The URL used to connect to the remote end.</param>
    /// <returns>The task object representing the asynchronous operation.</returns>
    /// <exception cref="TimeoutException">Thrown when the connection is not established within the startup timeout.</exception>
    public virtual async ValueTask StartAsync(Uri url)
    {
        if (client.State is WebSocketState.Closed or WebSocketState.Aborted)
        {
            client.Dispose();
            client = new ClientWebSocket();

            cts.Dispose();
            cts = new CancellationTokenSource();
        }

        if (client.State is not WebSocketState.None)
        {
            throw new BiDiException($"The WebSocket is already connected to {ConnectedUrl}; call the Stop method to disconnect before calling Start");
        }

        await LogAsync($"Opening connection to URL {url}").ConfigureAwait(false);

        var connected = false;
        var timeout = DateTime.UtcNow.Add(StartupTimeout);

        while (!connected && DateTime.UtcNow <= timeout)
        {
            try
            {
                await client.ConnectAsync(url, cts.Token).ConfigureAwait(false);
                connected = true;
                ConnectedUrl = url;
            }
            catch (WebSocketException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                client.Dispose();
                client = new ClientWebSocket();
            }
        }

        if (!connected)
        {
            throw new TimeoutException($"Could not connect to remote WebSocket server within {StartupTimeout.TotalSeconds} seconds");
        }

        dataReceiveTask = Task.Run(ReceiveDataAsync);
        await LogAsync($"Connection opened").ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously stops communication with the remote end of this connection.
    /// </summary>
    /// <returns>The task object representing the asynchronous operation.</returns>
    public virtual async ValueTask StopAsync()
    {
        await LogAsync($"Closing connection").ConfigureAwait(false);

        if (client.State is not WebSocketState.Open)
            await LogAsync($"Socket already closed (Socket state: {client.State})").ConfigureAwait(false);
        else
            await CloseClientWebSocketAsync().ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        if (dataReceiveTask is not null) await dataReceiveTask.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        ConnectedUrl = default;
    }

    /// <summary>
    /// Asynchronously sends data to the remote end of this connection.
    /// </summary>
    /// <param name="data">The data to be sent to the remote end of this connection.</param>
    /// <returns>The task object representing the asynchronous operation.</returns>
    public virtual async ValueTask SendDataAsync(ReadOnlyMemory<byte> data)
    {
        if (!IsActive) throw new BiDiException("The WebSocket has not been initialized; you must call the Start method before sending data");

        if (!await locker.WaitAsync(DataTimeout).ConfigureAwait(false))
            throw new BiDiException("Timed out waiting to access WebSocket for sending; only one send operation is permitted at a time.");

        if (OnLogMessage.CurrentObserverCount > 0) await LogAsync($"SEND >>> {Encoding.UTF8.GetString(data.Span)}", BiDiLogLevel.Debug).ConfigureAwait(false);

        await SendWebSocketDataAsync(data).ConfigureAwait(false);
        locker.Release();
    }

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}