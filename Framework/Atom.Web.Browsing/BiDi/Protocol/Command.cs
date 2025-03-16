using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Atom.Web.Browsing.BiDi.JsonConverters;

namespace Atom.Web.Browsing.BiDi.Protocol;

/// <summary>
/// Object containing data about a WebDriver Bidi command.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="Command"/> class.
/// </remarks>
/// <param name="commandId">The ID of the command.</param>
/// <param name="commandData">The settings for the command, including parameters.</param>
/// <param name="resultTypeInfo">Информация о типе.</param>
/// <param name="parametersTypeInfo">Информация о типе параметров.</param>
[JsonConverter(typeof(CommandJsonConverter))]
public class Command(long commandId, CommandParameters commandData, JsonTypeInfo parametersTypeInfo, JsonTypeInfo resultTypeInfo)
{
    private readonly TaskCompletionSource<CommandResult> taskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [JsonIgnore]
    internal JsonTypeInfo ParametersTypeInfo { get; } = parametersTypeInfo;

    [JsonIgnore]
    internal JsonTypeInfo ResultTypeInfo { get; } = resultTypeInfo;

    /// <summary>
    /// Gets the ID of the command.
    /// </summary>
    [JsonPropertyName("id")]
    public long CommandId { get; } = commandId;

    /// <summary>
    /// Gets the method name of the command.
    /// </summary>
    [JsonPropertyName("method")]
    public string CommandName => CommandParameters.MethodName;

    /// <summary>
    /// Gets the parameters of the command.
    /// </summary>
    [JsonPropertyName("params")]
    public CommandParameters CommandParameters { get; } = commandData;

    /// <summary>
    /// Gets additional properties to be serialized with this command.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, object?> AdditionalData => CommandParameters.AdditionalData;

    /// <summary>
    /// Gets or sets the result of the command.
    /// </summary>
    [JsonIgnore]
    public virtual CommandResult? Result
    {
        get => taskCompletionSource.Task.IsCompleted && !taskCompletionSource.Task.IsFaulted && !taskCompletionSource.Task.IsCanceled
            ? taskCompletionSource.Task.Result
            : null;

        set => Task.Run(() => taskCompletionSource.TrySetResult(value!)).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets or sets the exception thrown during execution of the command, if any.
    /// </summary>
    [JsonIgnore]
    public virtual Exception? ThrownException
    {
        get => taskCompletionSource.Task.IsFaulted ? taskCompletionSource.Task.Exception.InnerException : null;

        set
        {
            if (value is not null) taskCompletionSource.TrySetException(value);
        }
    }

    /// <summary>
    /// Waits for the command to complete or until the specified timeout elapses.
    /// </summary>
    /// <param name="timeout">The timeout to wait for the command to complete.</param>
    /// <returns><see langword="true"/> if the command completes before the timeout; otherwise <see langword="false"/>.</returns>
    public virtual async Task<bool> WaitForCompletionAsync(TimeSpan timeout)
    {
        var completedTask = await Task.WhenAny(taskCompletionSource.Task, Task.Delay(timeout)).ConfigureAwait(false);
        return completedTask == taskCompletionSource.Task;
    }

    /// <summary>
    /// Cancels the task used to wait for completion of this command.
    /// </summary>
    public virtual void Cancel() => taskCompletionSource.TrySetCanceled();
}