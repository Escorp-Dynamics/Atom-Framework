namespace Atom.Web.Browsing.BiDi.Protocol;

/// <summary>
/// An unhandled error received during execution of transport functions.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="UnhandledError"/> class.
/// </remarks>
/// <param name="errorType">The type of unhandled error.</param>
/// <param name="exception">The <see cref="Exception"/> to be thrown by the unhandled error.</param>
public class UnhandledError(UnhandledErrorType errorType, Exception exception)
{
    /// <summary>
    /// Gets the type of unhandled error.
    /// </summary>
    public UnhandledErrorType ErrorType { get; } = errorType;

    /// <summary>
    /// Gets the exception thrown by the unhandled error.
    /// </summary>
    public Exception Exception { get; } = exception;
}