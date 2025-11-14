namespace Atom.Debug;

/// <summary>
/// Represents the outcome of console command parsing.
/// </summary>
/// <param name="IsSuccess">Indicates whether parsing succeeded.</param>
/// <param name="Args">Arguments extracted from the command.</param>
/// <param name="IsCancellation">Indicates whether the command is a cancellation request.</param>
public readonly record struct ConsoleCommandParseResult(bool IsSuccess, IReadOnlyList<string> Args, bool IsCancellation)
{
    /// <summary>
    /// Gets an instance that represents a failed parse.
    /// </summary>
    public static ConsoleCommandParseResult Failed { get; } = new(IsSuccess: false, [], IsCancellation: false);
}
