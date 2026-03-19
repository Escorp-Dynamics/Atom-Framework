#pragma warning disable CA2215, S3881

using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

using Stream = Atom.IO.Stream;

namespace Atom.Net;

/// <summary>
/// Представляет базовую реализацию сетевого потока.
/// </summary>
/// <remarks>
/// Инициализирует новый экземпляр <see cref="NetworkStream"/>.
/// Сокет должен быть создан и (для TCP) подключён к удалённой стороне.
/// </remarks>
/// <param name="socket">Экземпляр сокета, которым будет управлять поток.</param>
/// <param name="ownsSocket">Если <see langword="true"/>, сокет будет закрыт при <see cref="Dispose(bool)"/></param>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public abstract partial class NetworkStream([NotNull] Socket socket, bool ownsSocket) : Stream
{
    /// <summary>
    /// Владелец сокета. Если true, сокет будет закрыт при Dispose().
    /// </summary>
    private readonly bool ownsSocket = ownsSocket;

    /// <summary>
    /// Флажки завершения работы: бит0=Rx закрыт, бит1=Tx закрыт.
    /// </summary>
    private int shutdownMask; // 0..3

    /// <summary>
    /// Флаг утилизации потока (0 — жив, 1 — Dispose() вызван).
    /// </summary>
    private bool isDisposed;

    /// <summary>
    /// Требовать ли «атомарной» синхронной записи (всё или исключение).
    /// </summary>
    protected virtual bool RequireAtomicSyncSend
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => default;
    }

    /// <summary>
    /// Обрабатывать ли <see cref="SocketError.WouldBlock"/> как «временную недоступность данных» и возвращать 0.
    /// </summary>
    protected virtual bool TreatWouldBlockAsZero
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => true;
    }

    /// <summary>
    /// Флаги чтения по умолчанию.
    /// </summary>
    protected virtual SocketFlags ReadFlags
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => SocketFlags.None;
    }

    /// <summary>
    /// Флаги записи по умолчанию.
    /// </summary>
    protected virtual SocketFlags WriteFlags
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => SocketFlags.None;
    }

    /// <inheritdoc/>
    public sealed override bool CanRead
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !Volatile.Read(ref isDisposed);
    }

    /// <inheritdoc/>
    public sealed override bool CanWrite
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !Volatile.Read(ref isDisposed);
    }

    /// <summary>
    /// Признак полузакрытия чтения.
    /// </summary>
    public bool IsReceiveShutdown
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Volatile.Read(ref shutdownMask) & 0b01) is not 0;
    }

    /// <summary>
    /// Признак полузакрытия записи.
    /// </summary>
    public bool IsSendShutdown
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (Volatile.Read(ref shutdownMask) & 0b10) is not 0;
    }

    /// <summary>
    /// Связанный с потоком экземпляр <see cref="System.Net.Sockets.Socket"/>.
    /// </summary>
    public Socket Socket
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref field);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected set => Volatile.Write(ref field, value);
    } = socket;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="NetworkStream"/>.
    /// Сокет должен быть создан и (для TCP) подключён к удалённой стороне.
    /// </summary>
    /// <param name="socket">Экземпляр сокета, которым будет управлять поток.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected NetworkStream([NotNull] Socket socket) : this(socket, ownsSocket: true) { }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length is 0) return default;
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        // Если чтение полузакрыто — как в NetworkStream, возвращаем 0 (EOF).
        if (IsReceiveShutdown) return default;

        var n = Socket.Receive(buffer, ReadFlags, out var err);

        if (err is SocketError.Success) return n; // 0 для закрытия соединения
        if (TreatWouldBlockAsZero && err is SocketError.WouldBlock) return default;

        throw new SocketException((int)err);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length is 0) return default;
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        if (IsReceiveShutdown) return default;

        int n;

        try
        {
            n = await Socket.ReceiveAsync(buffer, ReadFlags, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException se) when (TreatWouldBlockAsZero && se.SocketErrorCode is SocketError.WouldBlock)
        {
            return default;
        }

        return n; // 0 — закрыто
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length is 0) return;
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        if (IsSendShutdown) throw new IOException("Не удается выполнить запись: отправляющая сторона была отключена");

        var n = Socket.Send(buffer, WriteFlags, out var err);

        if (err is not SocketError.Success) throw new SocketException((int)err);
        if (RequireAtomicSyncSend && n != buffer.Length) throw new InvalidOperationException("Частичная отправка запрещена в соответствии с транспортной политикой");
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length is 0) return;
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed), this);

        if (IsSendShutdown) throw new IOException("Не удается выполнить запись: отправляющая сторона была отключена");

        var remaining = buffer;

        while (!remaining.IsEmpty)
        {
            var sent = await Socket.SendAsync(remaining, WriteFlags, cancellationToken).ConfigureAwait(false);
            if (sent <= 0) throw new IOException("Сокет не смог отправить данные");
            remaining = remaining[sent..];
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override void Flush() { /* без пользовательских буферов */ }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

}