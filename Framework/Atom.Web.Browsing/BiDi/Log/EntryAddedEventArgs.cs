using Atom.Web.Browsing.BiDi.Script;

namespace Atom.Web.Browsing.BiDi.Log;

/// <summary>
/// Object containing event data for the event raised when a log entry is added.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="EntryAddedEventArgs"/> class.
/// </remarks>
/// <param name="entry">The data describing the log entry.</param>
public class EntryAddedEventArgs(LogEntry entry) : BiDiEventArgs
{
    private readonly LogEntry entry = entry;

    /// <summary>
    /// Gets the type of log entry.
    /// </summary>
    public string Type => entry.Type;

    /// <summary>
    /// Gets the log level of the log entry.
    /// </summary>
    public LogLevel Level => entry.Level;

    /// <summary>
    /// Gets the source of the log entry.
    /// </summary>
    public Source Source => entry.Source;

    /// <summary>
    /// Gets the text of the log entry.
    /// </summary>
    public string? Text => entry.Text;

    /// <summary>
    /// Gets the timestamp of the log entry.
    /// </summary>
    public DateTime Timestamp => entry.Timestamp;

    /// <summary>
    /// Gets the stack trace of the log entry.
    /// </summary>
    public StackTrace? StackTrace => entry.StackTrace;

    /// <summary>
    /// Gets the method name of the log entry.
    /// </summary>
    public string? Method => entry is not ConsoleLogEntry consoleLogEntry ? null : consoleLogEntry.Method;

    /// <summary>
    /// Gets the read-only list of arguments for the log entry.
    /// </summary>
    public IList<RemoteValue>? Arguments => entry is not ConsoleLogEntry consoleLogEntry ? null : consoleLogEntry.Args;
}