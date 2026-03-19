#pragma warning disable CA2215, S3881

using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace Atom.Net;

public abstract partial class NetworkStream
{
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref isDisposed, value: true, default)) return;

        TryShutdownForDispose();
        DisposeOwnedSocketIfNeeded();
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ValueTask DisposeAsync()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Полузакрытие канала на чтение/запись.
    /// </summary>
    /// <param name="kind">Способ закрытия.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual void Shutdown(SocketShutdown kind)
    {
        if (Volatile.Read(ref isDisposed)) return;

        if (kind is SocketShutdown.Receive)
        {
            TryShutdownReceiveOnce();
            return;
        }

        if (kind is SocketShutdown.Send)
        {
            TryShutdownSendOnce();
            return;
        }

        TryShutdownBothOnce();
    }

    /// <summary>
    /// Полузакрытие канала на чтение/запись.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Shutdown() => Shutdown(SocketShutdown.Both);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TryShutdownForDispose()
    {
        try { Socket.Shutdown(SocketShutdown.Both); } catch { /* ignore */ }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DisposeOwnedSocketIfNeeded()
    {
        if (!ownsSocket) return;

        try { Socket.Dispose(); } catch { /* ignore */ }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TryShutdownReceiveOnce()
    {
        try { ShutdownReceiveOnce(); }
        catch { /* Игнорируем ошибки при полузакрытии. */ }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TryShutdownSendOnce()
    {
        try { ShutdownSendOnce(); }
        catch { /* Игнорируем ошибки при полузакрытии. */ }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TryShutdownBothOnce()
    {
        try { ShutdownBothOnce(); }
        catch { /* Игнорируем ошибки при полузакрытии. */ }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ShutdownReceiveOnce()
    {
        if ((Interlocked.Or(ref shutdownMask, 0b01) & 0b01) is not 0) return;
        Socket.Shutdown(SocketShutdown.Receive);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ShutdownSendOnce()
    {
        if ((Interlocked.Or(ref shutdownMask, 0b10) & 0b10) is not 0) return;
        Socket.Shutdown(SocketShutdown.Send);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ShutdownBothOnce()
    {
        if (Interlocked.Exchange(ref shutdownMask, 0b11) is 0b11) return;
        Socket.Shutdown(SocketShutdown.Both);
    }
}