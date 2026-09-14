using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Atom.Display.Wayland.Protocol;

/// <summary>
/// Соединение с клиентом Wayland.
/// </summary>
/// <remarks>
/// ★ Протокол передаёт не только байты, но и файловые дескрипторы — через вспомогательные данные
/// unix-сокета (<c lang="text">SCM_RIGHTS</c>). Без них не работает ничего существенного: буфер кадра
/// клиент отдаёт как дескриптор разделяемой памяти, раскладку клавиатуры композитор отдаёт так же.
/// В .NET доступа к вспомогательным данным нет, поэтому <c lang="text">sendmsg</c> и <c lang="text">recvmsg</c>
/// вызываются напрямую.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed partial class WaylandConnection : IDisposable
{
    private readonly Socket socket;
    private readonly int handle;
    private readonly List<int> pendingDescriptors = [];
    private readonly byte[] receiveBuffer = new byte[65536];
    private int receiveLength;
    private int isDisposed;

    internal WaylandConnection(Socket socket)
    {
        this.socket = socket;
        handle = (int)socket.Handle;
    }

    /// <summary>Клиент отключился.</summary>
    public bool IsClosed { get; private set; }

    /// <summary>
    /// Читает очередную порцию данных и разбирает сообщения.
    /// </summary>
    /// <remarks>
    /// ★ Сообщения отдаются по одному в обработчик, а не списком с копией тела каждого:
    /// сообщений тысячи в секунду, и каждая копия была мусором. Тело действительно только на время
    /// вызова обработчика — приёмный буфер переиспользуется.
    /// </remarks>
    public void Receive(Action<WaylandMessage> handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (!TryReceiveBytes())
            return;

        var offset = 0;

        while (WaylandMessage.TryParse(receiveBuffer.AsMemory(offset, receiveLength - offset), out var parsed, out var consumed))
        {
            offset += consumed;
            handle(parsed);
        }

        if (offset > 0)
        {
            Array.Copy(receiveBuffer, offset, receiveBuffer, 0, receiveLength - offset);
            receiveLength -= offset;
        }
    }

    private unsafe bool TryReceiveBytes()
    {
        // Вспомогательные данные: место под дескрипторы одного вызова.
        var controlSize = CmsgSpace(sizeof(int) * MaxDescriptorsPerMessage);
        var control = stackalloc byte[controlSize];

        fixed (byte* buffer = receiveBuffer)
        {
            var iov = new IoVector
            {
                Base = buffer + receiveLength,
                Length = (nuint)(receiveBuffer.Length - receiveLength),
            };

            var message = new MessageHeader
            {
                IoVectors = &iov,
                IoVectorCount = 1,
                Control = control,
                ControlLength = (nuint)controlSize,
            };

            var received = RecvMsg(handle, &message, MSG_DONTWAIT | MSG_CMSG_CLOEXEC);

            if (received == 0)
            {
                IsClosed = true;
                return false;
            }

            if (received < 0)
            {
                var error = Marshal.GetLastWin32Error();

                // EAGAIN означает «данных пока нет» — это нормальный ход событий.
                if (error is not (EAGAIN or EWOULDBLOCK or EINTR))
                    IsClosed = true;

                return false;
            }

            CollectDescriptors(&message);
            receiveLength += (int)received;
            return true;
        }
    }

    /// <summary>Забирает дескрипторы, пришедшие вместе с сообщениями.</summary>
    public int TakeDescriptor()
    {
        if (pendingDescriptors.Count == 0)
            throw new WaylandProtocolException("Клиент не передал файловый дескриптор, который требует сообщение.");

        var descriptor = pendingDescriptors[0];
        pendingDescriptors.RemoveAt(0);
        return descriptor;
    }

    /// <summary>Отправляет сообщение клиенту.</summary>
    public void Send(ReadOnlySpan<byte> message)
    {
        if (IsClosed)
            return;

        try
        {
            _ = socket.Send(message, SocketFlags.None);
        }
        catch (SocketException)
        {
            IsClosed = true;
        }
        catch (ObjectDisposedException)
        {
            IsClosed = true;
        }
    }

    /// <summary>
    /// Отправляет сообщение вместе с файловым дескриптором.
    /// </summary>
    /// <remarks>
    /// Так композитор отдаёт клавиатурную раскладку: клиент получает дескриптор и отображает его
    /// в память.
    /// </remarks>
    public unsafe void SendWithDescriptor(ReadOnlySpan<byte> message, int descriptor)
    {
        if (IsClosed)
            return;

        var controlSize = CmsgSpace(sizeof(int));
        var control = stackalloc byte[controlSize];
        new Span<byte>(control, controlSize).Clear();

        var header = (ControlMessageHeader*)control;
        header->Length = (nuint)CmsgLen(sizeof(int));
        header->Level = SOL_SOCKET;
        header->Type = SCM_RIGHTS;
        *(int*)((byte*)header + CmsgAlign(sizeof(ControlMessageHeader))) = descriptor;

        fixed (byte* buffer = message)
        {
            var iov = new IoVector { Base = buffer, Length = (nuint)message.Length };
            var messageHeader = new MessageHeader
            {
                IoVectors = &iov,
                IoVectorCount = 1,
                Control = control,
                ControlLength = (nuint)controlSize,
            };

            if (SendMsg(handle, &messageHeader, 0) < 0)
                IsClosed = true;
        }
    }

    private unsafe void CollectDescriptors(MessageHeader* message)
    {
        for (var header = FirstHeader(message); header is not null; header = NextHeader(message, header))
        {
            if (header->Level != SOL_SOCKET || header->Type != SCM_RIGHTS)
                continue;

            var dataOffset = CmsgAlign(sizeof(ControlMessageHeader));
            var count = ((int)header->Length - dataOffset) / sizeof(int);
            var values = (int*)((byte*)header + dataOffset);

            for (var index = 0; index < count; ++index)
                pendingDescriptors.Add(values[index]);
        }
    }

    private static unsafe ControlMessageHeader* FirstHeader(MessageHeader* message)
        => message->ControlLength >= (nuint)sizeof(ControlMessageHeader)
            ? (ControlMessageHeader*)message->Control
            : null;

    private static unsafe ControlMessageHeader* NextHeader(MessageHeader* message, ControlMessageHeader* current)
    {
        var next = (ControlMessageHeader*)((byte*)current + CmsgAlign((int)current->Length));
        var end = (byte*)message->Control + message->ControlLength;

        if ((byte*)next + sizeof(ControlMessageHeader) > end)
            return null;

        return next;
    }

    private static int CmsgAlign(int length)
    {
        var wordSize = IntPtr.Size;
        return (length + wordSize - 1) & ~(wordSize - 1);
    }

    private static unsafe int CmsgLen(int length) => CmsgAlign(sizeof(ControlMessageHeader)) + length;

    private static unsafe int CmsgSpace(int length) => CmsgAlign(sizeof(ControlMessageHeader)) + CmsgAlign(length);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) is not 0)
            return;

        foreach (var descriptor in pendingDescriptors)
            _ = Close(descriptor);

        pendingDescriptors.Clear();
        socket.Dispose();
    }

    private const int MaxDescriptorsPerMessage = 16;
    private const int SOL_SOCKET = 1;
    private const int SCM_RIGHTS = 1;
    private const int MSG_DONTWAIT = 0x40;
    private const int MSG_CMSG_CLOEXEC = 0x40000000;
    private const int EAGAIN = 11;
    private const int EWOULDBLOCK = 11;
    private const int EINTR = 4;

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct IoVector
    {
        public byte* Base;
        public nuint Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct MessageHeader
    {
        public void* Name;
        public uint NameLength;
        private readonly uint padding;
        public IoVector* IoVectors;
        public nuint IoVectorCount;
        public void* Control;
        public nuint ControlLength;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ControlMessageHeader
    {
        public nuint Length;
        public int Level;
        public int Type;
    }

    [LibraryImport("libc", EntryPoint = "recvmsg", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static unsafe partial nint RecvMsg(int socket, MessageHeader* message, int flags);

    [LibraryImport("libc", EntryPoint = "sendmsg", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static unsafe partial nint SendMsg(int socket, MessageHeader* message, int flags);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int Close(int descriptor);
}
