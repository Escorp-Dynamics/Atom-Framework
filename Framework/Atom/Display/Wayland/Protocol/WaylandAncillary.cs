using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Atom.Display.Wayland.Protocol;

/// <summary>
/// Передача файловых дескрипторов через unix-сокет.
/// </summary>
/// <remarks>
/// Протокол передаёт дескрипторы вспомогательными данными (<c lang="text">SCM_RIGHTS</c>): так уходят
/// буферы кадров и раскладка клавиатуры. В .NET доступа к ним нет, поэтому <c lang="text">sendmsg</c>
/// вызывается напрямую.
/// </remarks>
[SupportedOSPlatform("linux")]
internal static partial class WaylandAncillary
{
    /// <summary>Отправляет сообщение вместе с дескриптором.</summary>
    public static unsafe void SendWithDescriptor(Socket socket, ReadOnlySpan<byte> message, int descriptor)
    {
        ArgumentNullException.ThrowIfNull(socket);

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

            _ = SendMsg((int)socket.Handle, &messageHeader, 0);
        }
    }

    private static int CmsgAlign(int length)
    {
        var wordSize = IntPtr.Size;
        return (length + wordSize - 1) & ~(wordSize - 1);
    }

    private static unsafe int CmsgLen(int length) => CmsgAlign(sizeof(ControlMessageHeader)) + length;

    private static unsafe int CmsgSpace(int length) => CmsgAlign(sizeof(ControlMessageHeader)) + CmsgAlign(length);

    private const int SOL_SOCKET = 1;
    private const int SCM_RIGHTS = 1;

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

    [LibraryImport("libc", EntryPoint = "sendmsg", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static unsafe partial nint SendMsg(int socket, MessageHeader* message, int flags);
}
