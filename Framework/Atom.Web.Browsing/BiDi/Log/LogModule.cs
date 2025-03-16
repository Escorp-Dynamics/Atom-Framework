namespace Atom.Web.Browsing.BiDi.Log;

/// <summary>
/// The Log module contains functionality and events related to writing to the browser's console log.
/// </summary>
public sealed class LogModule : Module
{
    /// <summary>
    /// The name of the log module.
    /// </summary>
    public const string LogModuleName = "log";

    /// <summary>
    /// Initializes a new instance of the <see cref="LogModule"/> class.
    /// </summary>
    /// <param name="driver">The <see cref="BiDiDriver"/> used in the module commands and events.</param>
    public LogModule(BiDiDriver driver) : base(driver) => RegisterAsyncEventInvoker("log.entryAdded", JsonContext.Default.EventMessageLogEntry, OnEntryAddedAsync);

    /// <summary>
    /// Gets an observable event that notifies when an entry is added to the log.
    /// </summary>
    public ObservableEvent<EntryAddedEventArgs> OnEntryAdded { get; } = new();

    /// <summary>
    /// Gets the module name.
    /// </summary>
    public override string ModuleName => LogModuleName;

    private async ValueTask OnEntryAddedAsync(EventInfo<LogEntry> eventData)
    {
        var eventArgs = eventData.ToEventArgs<EntryAddedEventArgs>();
        await OnEntryAdded.NotifyObserversAsync(eventArgs).ConfigureAwait(false);
    }
}