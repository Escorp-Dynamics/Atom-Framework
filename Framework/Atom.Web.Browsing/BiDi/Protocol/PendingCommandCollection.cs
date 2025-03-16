#pragma warning disable CA1711
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Atom.Threading;

namespace Atom.Web.Browsing.BiDi.Protocol;

/// <summary>
/// Object containing a thread-safe collection of pending commands.
/// </summary>
public class PendingCommandCollection : IDisposable
{
    private readonly Locker locker = new();
    private readonly ConcurrentDictionary<long, Command> pendingCommands = [];
    private bool isDisposed;

    /// <summary>
    /// Gets a value indicating whether this collection is accepting commands.
    /// </summary>
    public bool IsAcceptingCommands { get; private set; } = true;

    /// <summary>
    /// Gets the number of commands currently in the collection.
    /// </summary>
    public int PendingCommandCount => pendingCommands.Count;

    /// <summary>
    /// Высвобождает ресурсы.
    /// </summary>
    /// <param name="disposing">Указывает, требуется ли освободить управляемые ресурсы.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref isDisposed, true, default)) return;

        if (disposing)
        {
            locker.Dispose();
        }
    }

    /// <summary>
    /// Adds a command to the collection.
    /// </summary>
    /// <param name="command">The command to add to the collection.</param>
    /// <exception cref="BiDiException">
    /// Thrown if the collection is no longer accepting commands, or the collection already
    /// contains a command with the ID of the command being added.
    /// </exception>
    public virtual void AddPendingCommand([NotNull] Command command)
    {
        locker.Wait();

        try
        {
            if (!IsAcceptingCommands) throw new BiDiException("Cannot add command; pending command collection is closed");

            if (!pendingCommands.TryAdd(command.CommandId, command)) throw new BiDiException($"Could not add command with id {command.CommandId}, as id already exists");
        }
        finally
        {
            locker.Release();
        }
    }

    /// <summary>
    /// Removes a command from the collection.
    /// </summary>
    /// <param name="commandId">The ID of the command to remove.</param>
    /// <param name="removedCommand">The command object removed from the collection.</param>
    /// <returns><see langword="true"/> if a command with the specified ID exists in the collection to be removed; otherwise, <see langword="false"/>.</returns>
    public virtual bool RemovePendingCommand(long commandId, out Command removedCommand) =>
        pendingCommands.TryRemove(commandId, out removedCommand!);

    /// <summary>
    /// Clears the collection, canceling all pending tasks of commands in the collection.
    /// </summary>
    /// <exception cref="BiDiException">
    /// Thrown if the collection has not been closed to the addition of new commands.
    /// </exception>
    public virtual void Clear()
    {
        if (IsAcceptingCommands) throw new BiDiException("Cannot clear the collection while it can accept new incoming commands; close it with the Close method first");

        foreach (var pendingCommand in pendingCommands.Values) pendingCommand.Cancel();
        pendingCommands.Clear();
    }

    /// <summary>
    /// Closes the collection, disallowing addition of any further commands to it.
    /// </summary>
    public virtual void Close()
    {
        locker.Wait();
        IsAcceptingCommands = false;
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