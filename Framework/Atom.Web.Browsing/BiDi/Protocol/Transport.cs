#pragma warning disable CA1031
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;

namespace Atom.Web.Browsing.BiDi.Protocol;

/// <summary>
/// The transport object used for serializing and deserializing JSON data used in the WebDriver Bidi protocol.
/// It uses a <see cref="Connection"/> object to communicate with the remote end, and does no further processing
/// of the objects serialized or deserialized. Consumers of this class are expected to handle things like awaiting
/// the response of a WebDriver BiDi command message.
/// </summary>
public class Transport : IDisposable
{
    private readonly Dictionary<string, JsonTypeInfo> eventMessageTypes = [];
    private readonly UnhandledErrorCollection unhandledErrors = new();
    private Channel<ReadOnlyMemory<byte>> queue = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions()
    {
        SingleReader = true,
        SingleWriter = true,
    });

    private PendingCommandCollection pendingCommands = new();
    private Task messageQueueProcessingTask = Task.CompletedTask;
    private long nextCommandId;
    private string terminationReason = "Normal shutdown";
    private bool isDisposed;

    /// <summary>
    /// Определяет, установлено ли подключение к браузеру.
    /// </summary>
    public bool IsConnected { get; protected set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Transport"/> class.
    /// </summary>
    public Transport() : this(new Connection()) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="Transport"/> class with a given command timeout and connection.
    /// </summary>
    /// <param name="connection">The connection used to communicate with the protocol remote end.</param>
    public Transport([NotNull] Connection connection)
    {
        Connection = connection;
        connection.OnDataReceived.AddObserver(OnConnectionDataReceivedAsync);
        connection.OnLogMessage.AddObserver(OnConnectionLogMessageAsync);
    }

    /// <summary>
    /// Gets an observable event that notifies when an event is received from the protocol.
    /// </summary>
    public ObservableEvent<EventReceivedEventArgs> OnEventReceived { get; } = new();

    /// <summary>
    /// Gets an observable event that notifies when an error is received from the protocol
    /// that is not the result of a command execution.
    /// </summary>
    public ObservableEvent<ErrorReceivedEventArgs> OnErrorEventReceived { get; } = new();

    /// <summary>
    /// Gets an observable event that notifies when an unknown message is received from the protocol.
    /// </summary>
    public ObservableEvent<UnknownMessageReceivedEventArgs> OnUnknownMessageReceived { get; } = new();

    /// <summary>
    /// Gets an observable event that notifies when a log message is written.
    /// </summary>
    public ObservableEvent<LogMessageEventArgs> OnLogMessage { get; } = new();

    /// <summary>
    /// Gets or sets a value indicating how this <see cref="Transport"/> should behave when an
    /// unhandled exception in a handler for a defined protocol is encountered. Defaults to
    /// ignoring exceptions, in which case, those exceptions will never be surfaced to the user.
    /// </summary>
    public TransportErrorBehavior EventHandlerExceptionBehavior
    {
        get => unhandledErrors.EventHandlerExceptionBehavior;
        set => unhandledErrors.EventHandlerExceptionBehavior = value;
    }

    /// <summary>
    /// Gets or sets a value indicating how this <see cref="Transport"/> should behave when a
    /// protocol error is encountered, such as invalid JSON or JSON missing required properties.
    /// Defaults to ignoring exceptions, in which case, those exceptions will never be surfaced
    /// to the user.
    /// </summary>
    public TransportErrorBehavior ProtocolErrorBehavior
    {
        get => unhandledErrors.ProtocolErrorBehavior;
        set => unhandledErrors.ProtocolErrorBehavior = value;
    }

    /// <summary>
    /// Gets or sets a value indicating how this <see cref="Transport"/> should behave when an
    /// unknown message is encountered, such as valid JSON that does not match any protocol data
    /// structure. Defaults to ignoring exceptions, in which case, those exceptions will never
    /// be surfaced to the user.
    /// </summary>
    public TransportErrorBehavior UnknownMessageBehavior
    {
        get => unhandledErrors.UnknownMessageBehavior;
        set => unhandledErrors.UnknownMessageBehavior = value;
    }

    /// <summary>
    /// Gets or sets a value indicating how this <see cref="Transport"/> should behave when an
    /// unexpected error is encountered, meaning an error response received with no corresponding
    /// command. Defaults to ignoring exceptions, in which case, those exceptions will never be
    /// surfaced to the user.
    /// </summary>
    public TransportErrorBehavior UnexpectedErrorBehavior
    {
        get => unhandledErrors.UnexpectedErrorBehavior;
        set => unhandledErrors.UnexpectedErrorBehavior = value;
    }

    /// <summary>
    /// Gets the ID of the last command to be added.
    /// </summary>
    protected long LastCommandId => nextCommandId;

    /// <summary>
    /// Gets the connection used to communicate with the browser.
    /// </summary>
    protected Connection Connection { get; }

    /// <summary>
    /// Asynchronously connects to the remote end web socket.
    /// </summary>
    /// <param name="websocketUri">The URI used to connect to the web socket.</param>
    /// <returns>The task object representing the asynchronous operation.</returns>
    public virtual async ValueTask ConnectAsync(Uri websocketUri)
    {
        if (IsConnected) throw new BiDiException($"The transport is already connected to {Connection.ConnectedUrl}; you must disconnect before connecting to another URL");

        if (!pendingCommands.IsAcceptingCommands) pendingCommands = new PendingCommandCollection();

        queue = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions()
        {
            SingleReader = true,
            SingleWriter = true,
        });

        unhandledErrors.ClearUnhandledErrors();

        nextCommandId = 0;
        messageQueueProcessingTask = Task.Run(ReadIncomingMessagesAsync);

        if (!Connection.IsActive) await Connection.StartAsync(websocketUri).ConfigureAwait(false);

        IsConnected = true;
    }

    /// <summary>
    /// Asynchronously disconnects from the remote end web socket.
    /// </summary>
    /// <returns>The task object representing the asynchronous operation.</returns>
    public virtual ValueTask DisconnectAsync() => DisconnectAsync(true);

    /// <summary>
    /// Asynchronously sends a command to the remote end.
    /// </summary>
    /// <param name="commandData">The command settings object containing all data required to execute the command.</param>
    /// <returns>The task object representing the asynchronous operation.</returns>
    /// <exception cref="BiDiException">Thrown if the command ID is already in use.</exception>
    /// <param name="parametersTypeInfo">Информация о типе параметров.</param>
    /// <param name="resultTypeInfo">Информация о типе.</param>
    public virtual async Task<Command> SendCommandAsync<TParams, TResult>(TParams commandData, JsonTypeInfo<TParams> parametersTypeInfo, JsonTypeInfo<CommandResponseMessage<TResult>> resultTypeInfo)
        where TParams : CommandParameters<TResult>
        where TResult : CommandResult
    {
        if (unhandledErrors.HasUnhandledErrors(TransportErrorBehavior.Terminate))
        {
            await DisconnectAsync(false).ConfigureAwait(false);
            throw CreateTerminationException();
        }

        if (!IsConnected) throw new BiDiException("Transport must be connected to a remote end to execute commands.");

        var commandId = GetNextCommandId();
        var command = new Command(commandId, commandData, parametersTypeInfo, resultTypeInfo);

        pendingCommands.AddPendingCommand(command);

        var commandJson = SerializeCommand(command);
        await Connection.SendDataAsync(commandJson).ConfigureAwait(false);

        return command;
    }

    /// <summary>
    /// Registers an event message to be recognized when received from the connection.
    /// </summary>
    /// <typeparam name="T">The type of data to be returned in the event.</typeparam>
    /// <param name="eventName">The name of the event.</param>
    /// <param name="typeInfo">Информация о типе.</param>
    public virtual void RegisterEventMessage<T>(string eventName, JsonTypeInfo<EventMessage<T>> typeInfo) => eventMessageTypes[eventName] = typeInfo;

    /// <summary>
    /// Increments the command ID for the command to be sent.
    /// </summary>
    /// <returns>The command ID for the command to be sent.</returns>
    protected long GetNextCommandId() => Interlocked.Increment(ref nextCommandId);

    /// <summary>
    /// Serializes a command for transmission across the WebSocket connection.
    /// </summary>
    /// <param name="command">The command to serialize.</param>
    /// <returns>The serialized JSON string representing the command.</returns>
    protected virtual ReadOnlyMemory<byte> SerializeCommand(Command command) => JsonSerializer.SerializeToUtf8Bytes(command, JsonContext.Default.Command);

    /// <summary>
    /// Deserializes an incoming message from the WebSocket connection.
    /// </summary>
    /// <param name="messageData">The message data to deserialize.</param>
    /// <returns>A JsonElement representing the parsed message.</returns>
    /// <exception cref="JsonException">
    /// Thrown when there is a syntax error in the incoming JSON.
    /// </exception>
    protected virtual JsonElement DeserializeMessage(ReadOnlyMemory<byte> messageData)
    {
        var document = JsonDocument.Parse(messageData);
        return document.RootElement;
    }

    /// <summary>
    /// Asynchronously disconnects from the remote end web socket.
    /// </summary>
    /// <param name="throwCollectedExceptions"> A value indicating whether to throw the collected exceptions.</param>
    /// <returns>The task object representing the asynchronous operation.</returns>
    protected virtual async ValueTask DisconnectAsync(bool throwCollectedExceptions)
    {
        pendingCommands.Close();
        await Connection.StopAsync().ConfigureAwait(false);

        queue.Writer.Complete();
        await queue.Reader.Completion.ConfigureAwait(false);

        pendingCommands.Clear();
        await messageQueueProcessingTask.ConfigureAwait(false);

        IsConnected = false;

        if (throwCollectedExceptions && unhandledErrors.HasUnhandledErrors(TransportErrorBehavior.Collect)) throw CreateTerminationException();
    }

    private async Task OnProtocolEventReceivedAsync(EventReceivedEventArgs e)
    {
        try
        {
            await OnEventReceived.NotifyObserversAsync(e).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CaptureUnhandledError(UnhandledErrorType.EventHandlerException, ex, $"Unhandled exception in user event handler for event name {e.EventName}");
        }
    }

    private ValueTask OnProtocolErrorEventReceivedAsync(ErrorReceivedEventArgs e) => OnErrorEventReceived.NotifyObserversAsync(e);

    private ValueTask OnProtocolUnknownMessageReceivedAsync(UnknownMessageReceivedEventArgs e) => OnUnknownMessageReceived.NotifyObserversAsync(e);

    private ValueTask OnConnectionDataReceivedAsync(ConnectionDataReceivedEventArgs e) => queue.Writer.WriteAsync(e.Data);

    private async ValueTask ReadIncomingMessagesAsync()
    {
        while (await queue.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (queue.Reader.TryRead(out var incomingMessage))
            {
                await ProcessMessageAsync(incomingMessage).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask ProcessMessageAsync(ReadOnlyMemory<byte> messageData)
    {
        var isProcessed = false;
        JsonElement messageRootElement = default;

        try
        {
            messageRootElement = DeserializeMessage(messageData);
        }
        catch (JsonException e)
        {
            await LogAsync($"Unexpected error parsing JSON message: {e.Message}", BiDiLogLevel.Error).ConfigureAwait(false);
            CaptureUnhandledError(UnhandledErrorType.ProtocolError, e, $"Invalid JSON in protocol message: {Encoding.UTF8.GetString(messageData.Span)}");
        }

        if (messageRootElement.ValueKind != JsonValueKind.Undefined)
        {
            if (messageRootElement.TryGetProperty("type", out var messageTypeToken) && messageTypeToken.ValueKind == JsonValueKind.String)
            {
                var messageType = messageTypeToken.GetString()!;

                if (messageType is "success")
                {
                    isProcessed = ProcessCommandResponseMessage(messageRootElement);

                    if (OnLogMessage.CurrentObserverCount > 0)
                        await LogAsync($"Command response message processed {Encoding.UTF8.GetString(messageData.Span)}", BiDiLogLevel.Trace).ConfigureAwait(false);
                }
                else if (messageType is "error")
                {
                    isProcessed = await ProcessErrorMessageAsync(messageRootElement).ConfigureAwait(false);

                    if (OnLogMessage.CurrentObserverCount > 0)
                        await LogAsync($"Error response message processed {Encoding.UTF8.GetString(messageData.Span)}", BiDiLogLevel.Trace).ConfigureAwait(false);
                }
                else if (messageType is "event")
                {
                    isProcessed = await ProcessEventMessageAsync(messageRootElement).ConfigureAwait(false);

                    if (OnLogMessage.CurrentObserverCount > 0)
                    {
                        await LogAsync($"Event message processed {Encoding.UTF8.GetString(messageData.Span)}", BiDiLogLevel.Trace).ConfigureAwait(false);
                    }
                }
            }
        }

        if (!isProcessed)
        {
            var message = Encoding.UTF8.GetString(messageData.Span);
            await OnProtocolUnknownMessageReceivedAsync(new UnknownMessageReceivedEventArgs(message)).ConfigureAwait(false);
            CaptureUnhandledError(UnhandledErrorType.UnknownMessage, new BiDiException($"Received unknown message from protocol connection: {message}"), "Unknown message from connection");
        }
    }

    private bool ProcessCommandResponseMessage(JsonElement message)
    {
        if (message.TryGetProperty("id", out var idToken) && idToken.ValueKind is JsonValueKind.Number && idToken.TryGetInt64(out var responseId))
        {
            if (pendingCommands.RemovePendingCommand(responseId, out var executedCommand))
            {
                try
                {
                    if (message.Deserialize(executedCommand.ResultTypeInfo) is CommandResponseMessage response)
                    {
                        var commandResult = response.Result;
                        commandResult.AdditionalData = response.AdditionalData;
                        executedCommand.Result = commandResult;
                    }
                }
                catch (Exception ex)
                {
                    executedCommand.ThrownException = new BiDiException($"Response did not contain properly formed JSON for response type (response JSON:{message})", ex);
                }

                return true;
            }
        }

        return false;
    }

    private async ValueTask<bool> ProcessErrorMessageAsync(JsonElement message)
    {
        try
        {
            var errorMessage = message.Deserialize(JsonContext.Default.ErrorResponseMessage);

            if (errorMessage is not null)
            {
                var result = errorMessage.GetErrorResponseData();

                if (errorMessage.CommandId.HasValue && pendingCommands.RemovePendingCommand(errorMessage.CommandId.Value, out var executedCommand))
                {
                    executedCommand.Result = result;
                }
                else
                {
                    await OnProtocolErrorEventReceivedAsync(new ErrorReceivedEventArgs(result)).ConfigureAwait(false);
                    CaptureUnhandledError(UnhandledErrorType.UnexpectedError, new BiDiException($"Received '{result.ErrorType}' error with no command ID: {result.ErrorMessage}"), "Received error with no command ID");
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            await LogAsync($"Unexpected error parsing error JSON: {ex.Message} (JSON: {message})", BiDiLogLevel.Error).ConfigureAwait(false);
            CaptureUnhandledError(UnhandledErrorType.ProtocolError, ex, $"Invalid JSON in protocol error response: {message}");
        }

        return false;
    }

    private async ValueTask<bool> ProcessEventMessageAsync(JsonElement message)
    {
        if (message.TryGetProperty("method", out var eventNameToken) && eventNameToken.ValueKind is JsonValueKind.String)
        {
            var eventName = eventNameToken.GetString()!;

            if (eventMessageTypes.TryGetValue(eventName, out var typeInfo))
            {
                try
                {
                    if (message.Deserialize(typeInfo) is EventMessage eventMessageData)
                    {
                        await OnProtocolEventReceivedAsync(new EventReceivedEventArgs(eventMessageData!)).ConfigureAwait(false);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    await LogAsync($"Unexpected error parsing event JSON: {ex.Message} (JSON: {message})", BiDiLogLevel.Error).ConfigureAwait(false);
                    CaptureUnhandledError(UnhandledErrorType.ProtocolError, ex, $"Invalid JSON in event message: {message}");
                }
            }
        }

        return false;
    }

    private void CaptureUnhandledError(UnhandledErrorType errorType, Exception ex, string terminalReason)
    {
        var isTerminalError = false;

        switch (errorType)
        {
            case UnhandledErrorType.ProtocolError:
                isTerminalError = ProtocolErrorBehavior is TransportErrorBehavior.Terminate;
                break;
            case UnhandledErrorType.UnknownMessage:
                isTerminalError = UnknownMessageBehavior is TransportErrorBehavior.Terminate;
                break;
            case UnhandledErrorType.UnexpectedError:
                isTerminalError = UnexpectedErrorBehavior is TransportErrorBehavior.Terminate;
                break;
            case UnhandledErrorType.EventHandlerException:
                isTerminalError = EventHandlerExceptionBehavior is TransportErrorBehavior.Terminate;
                break;
        }

        unhandledErrors.AddUnhandledError(errorType, ex);
        if (isTerminalError) terminationReason = terminalReason;
    }

    private ValueTask LogAsync(string message, BiDiLogLevel level) => OnLogMessage.NotifyObserversAsync(new LogMessageEventArgs(message, level, "Transport"));

    private ValueTask OnConnectionLogMessageAsync(LogMessageEventArgs e) => OnLogMessage.NotifyObserversAsync(e);

    private Exception CreateTerminationException()
    {
        var message = $"Unhandled exception during transport operations. Transport was terminated with the following reason: {terminationReason}";

        return unhandledErrors.Exceptions.Count is 1
            ? new BiDiException(message, unhandledErrors.Exceptions[0])
            : new AggregateException(message, unhandledErrors.Exceptions);
    }

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    /// <param name="disposing">Указывает, требуется ли высвобождать управляемые ресурсы.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;

        if (disposing)
        {
            pendingCommands.Dispose();
        }
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